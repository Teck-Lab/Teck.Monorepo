using Keycloak.AuthServices.Authentication;
using Pricing.Application.Database;
using Pricing.Application.Pricing;
using Pricing.Host.Database;
using Pricing.Host.Infrastructure;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Behaviors;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.Messaging.DeadLetter;
using Teck.ServiceDefaults;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddPricingPersistence();
builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection("Pricing"));
builder.Services.AddScoped<IExchangeRateProvider, ExchangeRateProviderStub>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.Host.UseWolverine(opts =>
{
    // Handlers live in the Pricing.Application assembly; Wolverine scans the entry assembly by
    // default, so include the application assembly for runtime handler discovery.
    opts.Discovery.IncludeAssembly(typeof(PricingDbContext).Assembly);
    opts.AddTeckBehaviors();
    opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
});
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);
