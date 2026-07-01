using Baskets.Application.Baskets;
using Baskets.Application.Database;
using Baskets.Host.Database;
using Baskets.Host.Infrastructure;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Behaviors;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.Messaging.DeadLetter;
using Teck.ServiceDefaults;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddBasketPersistence();
builder.Services.Configure<BasketOptions>(builder.Configuration.GetSection("Basket"));
builder.Services.AddScoped<IBasketIdentityAccessor, BasketIdentityAccessor>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.Host.UseWolverine(opts =>
{
    // Command, query and event handlers live in the Baskets.Application assembly, but Wolverine
    // only scans the entry assembly (Basket.Host) by default. Include the application assembly so
    // handlers are discovered at runtime in every environment (not only in tests).
    opts.Discovery.IncludeAssembly(typeof(BasketDbContext).Assembly);
    opts.AddTeckBehaviors();
    opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
});
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);
