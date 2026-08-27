extern alias CustomerHost;
extern alias OrderHost;
extern alias PricingHost;
extern alias PricingApplication;

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Customers.Application.Database;
using Customers.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Teck.LocalIdentity;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>
/// Starts equivalent imported Keycloak realms for the provisioned and pre-command control states.
/// The provisioned state runs the same reconciliation components as the local identity command;
/// the control deliberately imports only the committed realm.
/// </summary>
public sealed class LocalIdentityKeycloakFixture : IAsyncLifetime
{
    internal const string Realm = "teck";
    internal const string GatewayClientId = "public-gateway";
    internal const string GatewayClientSecret = "dev-secret-change-me";
    internal const string DeveloperUsername = "dev@teck.local";
    internal const string DeveloperPassword = "local-only-dev-password-not-for-production";
    internal const string ReaderUsername = "dev-reader@teck.local";
    internal const string ReaderPassword = "local-only-dev-reader-password-not-for-production";
    private const string AdminUsername = "admin";
    private const string AdminPassword = "local-only-keycloak-admin-password-not-for-production";
    private const string KeycloakImage = "quay.io/keycloak/keycloak:26.6.0";
    private static readonly Regex DatabaseIdentifierPattern = new("^[a-z0-9_]{1,63}$", RegexOptions.CultureInvariant);
    private PostgreSqlContainer? postgres;
    private KeycloakContainer? provisioned;
    private KeycloakContainer? unprovisioned;
    private RabbitMqContainer? rabbitMq;
    private string? provisionedConnectionString;
    private string? unprovisionedConnectionString;
    private string? orderConnectionString;
    private string? pricingConnectionString;

    /// <summary>Gets the Keycloak state after reconciliation and tenant registration.</summary>
    internal LocalIdentityTestInstance Provisioned => new(
        GetRequired(provisioned),
        GetRequired(provisionedConnectionString),
        GetRequired(orderConnectionString),
        GetRequired(pricingConnectionString),
        RabbitMqConnectionString,
        true);

    /// <summary>Gets the imported-realm-only state before the documented reconciliation command runs.</summary>
    internal LocalIdentityTestInstance Unprovisioned => new(
        GetRequired(unprovisioned),
        GetRequired(unprovisionedConnectionString),
        GetRequired(orderConnectionString),
        GetRequired(pricingConnectionString),
        RabbitMqConnectionString,
        false);

    /// <summary>Gets the migrated shared database used by the in-process order host.</summary>
    internal string OrderConnectionString => GetRequired(orderConnectionString);

    /// <summary>Gets the migrated shared database used by the in-process pricing host.</summary>
    internal string PricingConnectionString => GetRequired(pricingConnectionString);

    /// <summary>Gets the RabbitMQ connection string required by the real Wolverine service hosts.</summary>
    internal string RabbitMqConnectionString => NormalizeRabbitMqConnectionString(GetRequired(rabbitMq).GetConnectionString());

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        string realmPath = Path.Combine(FindRepositoryRoot(), "src", "aspire", "Teck.AppHost", "realms", "teck-realm.json");
        postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        rabbitMq = new RabbitMqBuilder("rabbitmq:3-management")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();
        provisioned = CreateKeycloak(realmPath);
        unprovisioned = CreateKeycloak(realmPath);

        await Task.WhenAll(postgres.StartAsync(), rabbitMq.StartAsync(), provisioned.StartAsync(), unprovisioned.StartAsync()).ConfigureAwait(false);
        provisionedConnectionString = await CreateMigratedDatabaseAsync<CustomerDbContext>("local_identity_provisioned", typeof(CustomerHost::Program).Assembly.GetName().Name!).ConfigureAwait(false);
        unprovisionedConnectionString = await CreateMigratedDatabaseAsync<CustomerDbContext>("local_identity_unprovisioned", typeof(CustomerHost::Program).Assembly.GetName().Name!).ConfigureAwait(false);
        orderConnectionString = await CreateMigratedDatabaseAsync<Orders.Application.Database.OrderDbContext>("local_identity_order", typeof(OrderHost::Program).Assembly.GetName().Name!).ConfigureAwait(false);
        pricingConnectionString = await CreateMigratedDatabaseAsync<PricingApplication::Pricing.Application.Database.PricingDbContext>("local_identity_pricing", typeof(PricingHost::Program).Assembly.GetName().Name!).ConfigureAwait(false);
        await ReconcileProvisionedInstanceAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (unprovisioned is not null)
        {
            await unprovisioned.DisposeAsync().ConfigureAwait(false);
        }

        if (provisioned is not null)
        {
            await provisioned.DisposeAsync().ConfigureAwait(false);
        }

        if (postgres is not null)
        {
            await postgres.DisposeAsync().ConfigureAwait(false);
        }

        if (rabbitMq is not null)
        {
            await rabbitMq.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Obtains a real password-grant access token from the selected Keycloak instance.</summary>
    internal static async Task<string> GetReaderTokenAsync(LocalIdentityTestInstance instance, CancellationToken cancellationToken = default)
        => await GetTokenAsync(instance, ReaderUsername, ReaderPassword, cancellationToken).ConfigureAwait(false);

    /// <summary>Obtains a real password-grant access token for a committed local user from the selected Keycloak instance.</summary>
    internal static async Task<string> GetTokenAsync(
        LocalIdentityTestInstance instance,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { BaseAddress = new Uri(instance.Keycloak.GetBaseAddress(), UriKind.Absolute) };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"realms/{Realm}/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password),
                new KeyValuePair<string, string>("scope", "openid organization:*"),
            ]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{GatewayClientId}:{GatewayClientSecret}")));

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Assert.True(response.IsSuccessStatusCode, $"Password grant returned {(int)response.StatusCode}: {body}");
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak password grant did not return access_token.");
    }

    /// <summary>Returns the actual tenant record registered by the provisioner, if any.</summary>
    internal static async Task<Tenant?> FindTenantAsync(LocalIdentityTestInstance instance, string tenantId, CancellationToken cancellationToken = default)
    {
        await using CustomerDbContext database = CreateCustomerContext(instance.CustomerConnectionString);
        return await database.Tenants.SingleOrDefaultAsync(tenant => tenant.Id.ToString() == tenantId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parses the access token without validating it; Keycloak issued it moments earlier over the fixture's private endpoint.</summary>
    internal static JwtSecurityToken ReadToken(string accessToken) => new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

    private static KeycloakContainer CreateKeycloak(string realmPath) => new KeycloakBuilder(KeycloakImage)
        .WithUsername(AdminUsername)
        .WithPassword(AdminPassword)
        .WithRealm(realmPath)
        .Build();

    private async Task ReconcileProvisionedInstanceAsync()
    {
        LocalIdentityTestInstance instance = Provisioned;
        var options = new LocalIdentityOptions
        {
            BaseUrl = instance.Keycloak.GetBaseAddress(),
            AdminUsername = AdminUsername,
            AdminPassword = AdminPassword,
            Realm = Realm,
        };
        using var client = new HttpClient { BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute) };
        string root = FindRepositoryRoot();
        using JsonDocument realm = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "src", "aspire", "Teck.AppHost", "realms", "teck-realm.json")).ConfigureAwait(false));
        using JsonDocument organizations = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "src", "aspire", "Teck.AppHost", "realms", "local-organizations.json")).ConfigureAwait(false));

        await new RealmReconciler(client, options).ReconcileAsync(realm, CancellationToken.None).ConfigureAwait(false);
        var provisioner = new LocalIdentityProvisioner(
            new OrganizationReconciler(client, options),
            TenantRegistryWriter.Create(instance.CustomerConnectionString));
        await provisioner.ProvisionAsync(organizations, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<string> CreateMigratedDatabaseAsync<TDbContext>(string databaseName, string migrationsAssembly)
        where TDbContext : DbContext
    {
        ValidateDatabaseIdentifier(databaseName);
        PostgreSqlContainer database = GetRequired(postgres);
        await using var admin = new global::Npgsql.NpgsqlConnection(database.GetConnectionString());
        await admin.OpenAsync().ConfigureAwait(false);
        await using (var command = admin.CreateCommand())
        {
            // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli -- ValidateDatabaseIdentifier restricts databaseName to safe PostgreSQL identifier characters.
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        string connectionString = new global::Npgsql.NpgsqlConnectionStringBuilder(database.GetConnectionString()) { Database = databaseName }.ConnectionString;
        var options = new DbContextOptionsBuilder<TDbContext>()
            .UseNpgsql(connectionString, builder => builder.MigrationsAssembly(migrationsAssembly))
            .Options;
        await using TDbContext databaseContext = (TDbContext)Activator.CreateInstance(typeof(TDbContext), options, null!)!;
        await databaseContext.Database.MigrateAsync().ConfigureAwait(false);
        return connectionString;
    }

    private static CustomerDbContext CreateCustomerContext(string connectionString) => new(
        new DbContextOptionsBuilder<CustomerDbContext>().UseNpgsql(connectionString).Options,
        null!);

    private static void ValidateDatabaseIdentifier(string databaseName)
    {
        if (string.IsNullOrEmpty(databaseName) || !DatabaseIdentifierPattern.IsMatch(databaseName))
        {
            throw new ArgumentException("Database identifiers must contain only lowercase letters, digits, and underscores and be at most 63 characters long.", nameof(databaseName));
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "aspire", "Teck.AppHost", "realms", "teck-realm.json")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the committed Teck realm JSON.");
    }

    private static T GetRequired<T>(T? value) where T : class => value ?? throw new InvalidOperationException("Fixture initialization has not completed.");

    private static string NormalizeRabbitMqConnectionString(string raw) => raw
        .Replace("rabbitmqs://", "amqps://", StringComparison.OrdinalIgnoreCase)
        .Replace("rabbitmq://", "amqp://", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One independently-addressable local-identity state used by the focused assertions.</summary>
internal sealed record LocalIdentityTestInstance(
    KeycloakContainer Keycloak,
    string CustomerConnectionString,
    string OrderConnectionString,
    string PricingConnectionString,
    string RabbitMqConnectionString,
    bool IsProvisioned);

/// <summary>Owns one fixture for the runtime Keycloak and tenant-registry contract tests.</summary>
[CollectionDefinition("LocalIdentityKeycloak", DisableParallelization = true)]
public sealed class LocalIdentityKeycloakCollection : ICollectionFixture<LocalIdentityKeycloakFixture>;
