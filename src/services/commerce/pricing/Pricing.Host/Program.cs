using Keycloak.AuthServices.Authentication;
using Pricing.Application.Database;
using Pricing.Application.Pricing;
using Pricing.Host.Database;
using Pricing.Host.Infrastructure;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.FeatureFlags;
using SharedKernel.Infrastructure.Hosting;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddPricingPersistence();
builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection("Pricing"));
builder.Services.AddTeckFeatureFlags(builder.Configuration);
builder.Services.AddScoped<IExchangeRateProvider, ExchangeRateProviderStub>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.AddTeckMessaging(typeof(PricingDbContext).Assembly, "PricingWrite");
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);
