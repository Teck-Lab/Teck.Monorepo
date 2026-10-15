using Catalog.Application.Database;
using Catalog.Host.Database;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.OpenApi;
using SharedKernel.Infrastructure.Options;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddCatalogPersistence();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.AddTeckMessaging(typeof(CatalogDbContext).Assembly, "CatalogWrite");
var appOptions = builder.Configuration.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();
builder.AddOpenApiInfrastructure(appOptions);
var app = builder.Build();
app.UseTeckService();
app.UseOpenApiInfrastructure(appOptions);
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);
