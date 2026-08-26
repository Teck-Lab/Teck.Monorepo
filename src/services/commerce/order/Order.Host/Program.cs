using Keycloak.AuthServices.Authentication;
using Orders.Application.Database;
using Orders.Application.Orders;
using Orders.Host.Database;
using Orders.Host.Infrastructure;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Hosting;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddOrderPersistence();
builder.Services.AddScoped<IOrderIdentityAccessor, OrderIdentityAccessor>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.AddTeckMessaging(typeof(OrderDbContext).Assembly, "OrderWrite");
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);
