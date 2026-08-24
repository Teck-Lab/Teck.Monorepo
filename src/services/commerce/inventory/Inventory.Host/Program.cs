using Inventories.Application.Database;
using Inventories.Application.Inventory;
using Inventories.Host.Database;
using Inventories.Host.Infrastructure;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Hosting;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddInventoryPersistence();
builder.Services.Configure<InventoryOptions>(builder.Configuration.GetSection("Inventory"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInventoryFeatureFlags(builder.Configuration);
builder.Services.AddHostedService<ReservationExpirySweepService>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.AddTeckMessaging(typeof(InventoryDbContext).Assembly, "InventoryWrite");
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);
