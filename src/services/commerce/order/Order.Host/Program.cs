using Keycloak.AuthServices.Authentication;
using Orders.Application.Database;
using Orders.Host.Database;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Behaviors;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.Messaging.DeadLetter;
using SharedKernel.Infrastructure.Observability;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
builder.AddTeckCloudObservability();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddOrderPersistence();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.Host.UseWolverine(opts =>
{
    // Command, query and event handlers live in the Orders.Application assembly, but Wolverine
    // only scans the entry assembly (Order.Host) by default. Include the application assembly so
    // handlers are discovered at runtime in every environment (not only in tests).
    opts.Discovery.IncludeAssembly(typeof(OrderDbContext).Assembly);
    opts.AddTeckBehaviors();
    opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
});
var app = builder.Build();
app.UseTeckService();
return await app.RunTeckServiceAsync(args);
