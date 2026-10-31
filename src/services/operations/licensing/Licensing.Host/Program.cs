using Licensing.Application;
using Licensing.Application.Database;
using Licensing.Host;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Hosting;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.Services.AddDbContext<LicensingDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("LicensingWrite") ?? builder.Configuration.GetConnectionString("Default"),
        npgsql => npgsql.MigrationsAssembly(typeof(Program).Assembly.FullName)));
builder.Services.Configure<LicenseKeyOptions>(builder.Configuration.GetSection(LicenseKeyOptions.SectionName));
builder.Services.AddSingleton<ILicenseSigningKeyProvider>(services => services.GetRequiredService<Microsoft.Extensions.Options.IOptions<LicenseKeyOptions>>().Value);
builder.Services.AddScoped<ILicenseStore, LicenseDbStore>();
builder.Services.AddScoped<LicenseIssuer>();
builder.Services.AddSingleton(_ => new ReadinessSigningKey(builder.Configuration["Readiness:LicenseKey"]));
builder.Services.AddSingleton(_ => new SubscriptionPaymentVerificationKeys(
    builder.Configuration.GetSection("PaymentProof:TrustedPublicKeys").Get<string[]>()));
builder.AddTeckMessaging(typeof(LicenseIssuer).Assembly, "LicensingWrite", Wolverine.Transports.NamingSource.FromHandlerType);
var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/licenses/verify", LicenseVerificationEndpoint.VerifyAsync);
return await app.RunTeckServiceAsync<LicensingDbContext>(args);

/// <summary>Entry point exposed to application composition tests.</summary>
public partial class Program;
