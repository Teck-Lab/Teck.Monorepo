using Npgsql;
using SharedKernel.Events;
using SharedKernel.Infrastructure;
using SharedKernel.Infrastructure.Database;
using SharedKernel.Infrastructure.Database.MultiTenant;
using SharedKernel.Infrastructure.Hosting;
using Teck.ServiceDefaults;
using Teck.TenantDatabase.Reconciler;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Kubernetes;
using VaultSharp.V1.AuthMethods.Token;
using Wolverine.Transports;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
string write = CodegenConnectionString.ResolveRequired(builder.Configuration, "TenantDatabaseWrite", "Default");
builder.Services.AddSingleton(NpgsqlDataSource.Create(write));
builder.Services.AddSingleton<PostgresTenantDatabaseRequestStore>();
builder.Services.AddSingleton<ITenantDatabaseRequestStore>(services => services.GetRequiredService<PostgresTenantDatabaseRequestStore>());
builder.Services.AddSingleton<ITenantPurgeStore>(services => services.GetRequiredService<PostgresTenantDatabaseRequestStore>());
builder.Services.AddSingleton(_ => new TenantPurgeVerificationKeys(
    builder.Configuration.GetSection("PurgeApproval:TrustedPublicKeys").Get<string[]>()));
builder.Services.AddSingleton(_ => new TenantPurgeRetentionPolicy(int.TryParse(
    builder.Configuration["PurgeApproval:RecoveryDays"], out int days) ? TimeSpan.FromDays(days) : TimeSpan.Zero));
builder.Services.AddSingleton(_ => new SubscriptionPaymentVerificationKeys(
    builder.Configuration.GetSection("PaymentProof:TrustedPublicKeys").Get<string[]>()));
builder.Services.Configure<BillingPurgeFenceClientOptions>(builder.Configuration.GetSection("BillingPurgeFenceClient"));
builder.Services.AddHttpClient<IBillingPurgeFenceClient, BillingPurgeFenceClient>();
builder.Services.AddSingleton<HttpClient>(_ => InClusterKubernetesClient.Create(builder.Configuration));
builder.Services.AddSingleton<ITenantDatabaseRoleRevoker>(services =>
    new PostgresTenantRoleRevoker(services.GetRequiredService<HttpClient>(),
        cluster => cluster + "-rw.tenant-databases.svc.cluster.local", 5432,
        string.IsNullOrWhiteSpace(builder.Configuration["Postgres:RootCertificate"]) ? SslMode.Require : SslMode.VerifyCA,
        builder.Configuration["Postgres:RootCertificate"]));
builder.Services.AddSingleton<ITenantCnpgBackend>(services =>
    new KubernetesCnpgBackend(services.GetRequiredService<HttpClient>(),
        services.GetRequiredService<ITenantDatabaseRoleRevoker>(),
        services.GetRequiredService<ITenantOpenBaoConnectionStore>(),
        builder.Configuration["TenantDatabase:VolumeSnapshotClass"]));
builder.Services.AddSingleton<IVaultClient>(_ => CreateOpenBaoClient(builder.Configuration));
builder.Services.AddSingleton<ITenantOpenBaoConnectionStore>(services =>
    new OpenBaoTenantConnectionStore(services.GetRequiredService<IVaultClient>(),
        builder.Configuration["OpenBao:KvMount"] ?? "teck-cloud"));
builder.Services.AddSingleton<ITenantMigrationConnectionProvider>(services =>
    new KubernetesTenantMigrationConnections(services.GetRequiredService<HttpClient>(),
        services.GetRequiredService<ITenantOpenBaoConnectionStore>(),
        cluster => cluster + "-rw.tenant-databases.svc.cluster.local", 5432,
        string.IsNullOrWhiteSpace(builder.Configuration["Postgres:RootCertificate"]) ? SslMode.Require : SslMode.VerifyCA,
        builder.Configuration["Postgres:RootCertificate"]));
builder.Services.AddSingleton<ITenantSchemaMigrator>(services =>
    new KubernetesTenantSchemaMigrator(services.GetRequiredService<HttpClient>(),
        services.GetRequiredService<ITenantMigrationConnectionProvider>(),
        builder.Configuration.GetSection("TenantDatabase:ServiceImages").Get<Dictionary<string, string>>() ?? [],
        builder.Configuration["Keycloak:realm"] ?? "", builder.Configuration["Keycloak:auth-server-url"] ?? ""));
builder.Services.AddSingleton(_ => new DatabaseReadinessSigningKey(builder.Configuration["Readiness:DatabaseKey"]));
builder.Services.AddSingleton<ITenantDatabaseReadinessPublisher, WolverineTenantDatabaseReadinessPublisher>();
builder.Services.AddSingleton<TenantDatabaseReconciler>();
builder.Services.AddSingleton<TenantDatabaseWorker>();
builder.Services.AddSingleton<ITenantRecoveredDatabaseInspector>(services =>
    new NpgsqlTenantRecoveredDatabaseInspector(services.GetRequiredService<HttpClient>(),
        cluster => cluster + "-rw.tenant-databases.svc.cluster.local", 5432, SslMode.VerifyCA,
        builder.Configuration["Postgres:RootCertificate"]));
builder.Services.AddSingleton<ITenantRecoverableBackupVerifier>(services =>
    new KubernetesTenantBackupVerifier(services.GetRequiredService<HttpClient>(),
        services.GetRequiredService<ITenantRecoveredDatabaseInspector>(),
        builder.Configuration["TenantDatabase:VolumeSnapshotClass"]));
builder.Services.AddSingleton<ITenantCnpgPurger>(services =>
    new KubernetesTenantCnpgPurger(services.GetRequiredService<HttpClient>()));
builder.Services.AddSingleton<TenantPurgeCoordinator>();
builder.Services.Configure<TenantDatabasePollingOptions>(builder.Configuration.GetSection("TenantDatabase:Polling"));
builder.Services.AddHostedService<TenantDatabasePollingService>();
builder.Services.AddHealthChecks().AddCheck<TenantDatabaseReadinessCheck>("tenant-database-store", tags: ["ready"]);
builder.AddTeckMessaging(typeof(SignedTenantDatabaseIntake).Assembly, "TenantDatabaseWrite", NamingSource.FromHandlerType);

var app = builder.Build();
if (!CodeGenerationDetector.IsRunningGeneration())
{
    await using var scope = app.Services.CreateAsyncScope();
    await using var connection = await scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
    await using var migration = connection.CreateCommand();
    migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
        "tenant-database", "001_create_tenant_database_requests.sql"));
    await migration.ExecuteNonQueryAsync();
}

app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);

static IVaultClient CreateOpenBaoClient(IConfiguration config)
{
    var options = config.GetSection(OpenBaoOptions.Section).Get<OpenBaoOptions>() ?? new OpenBaoOptions();
    if (string.IsNullOrWhiteSpace(options.Url))
    {
        throw new InvalidOperationException("OpenBao:Url is required for tenant connections.");
    }

    IAuthMethodInfo auth;
    if (!string.IsNullOrWhiteSpace(options.Token))
    {
        auth = new TokenAuthMethodInfo(options.Token);
    }
    else
    {
        if (string.IsNullOrWhiteSpace(options.Role))
        {
            throw new InvalidOperationException("OpenBao:Role is required for Kubernetes authentication.");
        }

        string jwt = File.ReadAllText("/var/run/secrets/kubernetes.io/serviceaccount/token");
        auth = new KubernetesAuthMethodInfo(options.AuthPath, options.Role, jwt);
    }

    return new VaultClient(new VaultClientSettings(options.Url, auth));
}

/// <summary>Executable tenant database worker entry point.</summary>
public partial class Program;
