using Catalog.Application.Database;
using Catalog.Host.Database;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.OpenApi;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.Configure<SharedKernel.Infrastructure.Options.AppOptions>(builder.Configuration.GetSection("App"));
builder.AddOpenApiInfrastructure(builder.Configuration.GetSection("App").Get<SharedKernel.Infrastructure.Options.AppOptions>() ?? new SharedKernel.Infrastructure.Options.AppOptions());
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddCatalogPersistence();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.AddTeckMessaging(typeof(CatalogDbContext).Assembly, "CatalogWrite");
var app = builder.Build();
app.UseTeckService();
app.UseOpenApiInfrastructure(builder.Configuration.GetSection("App").Get<SharedKernel.Infrastructure.Options.AppOptions>() ?? new SharedKernel.Infrastructure.Options.AppOptions());
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync<CatalogDbContext>(args);
