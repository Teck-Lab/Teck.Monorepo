using Baskets.Application.Baskets;
using Baskets.Application.Database;
using Baskets.Host.Database;
using Baskets.Host.Infrastructure;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.FeatureFlags;
using SharedKernel.Infrastructure.Hosting;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddBasketPersistence();
builder.Services.Configure<BasketOptions>(builder.Configuration.GetSection("Basket"));
builder.Services.AddTeckFeatureFlags(builder.Configuration);
builder.Services.AddScoped<IBasketIdentityAccessor, BasketIdentityAccessor>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.AddTeckMessaging(typeof(BasketDbContext).Assembly, "BasketWrite");
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);
