using Baskets.Application.Baskets;
using Baskets.Application.Database;
using Baskets.Host.Database;
using Baskets.Host.Infrastructure;
using Finbuckle.MultiTenant.Extensions;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.FeatureFlags;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
// A guest token identifies a basket, not a tenant. A storefront deployment must supply
// its trusted tenant ID through configuration; request headers never select it.
builder.Services.AddMultiTenant<TenantDetails>().WithDelegateStrategy(context =>
{
    if (context is not HttpContext http || http.User.Identity?.IsAuthenticated == true)
    {
        return Task.FromResult<string?>(null);
    }

    string? tenantId = builder.Configuration["Basket:StorefrontTenantId"];
    return Task.FromResult(string.IsNullOrWhiteSpace(tenantId) ? null : tenantId);
});
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
