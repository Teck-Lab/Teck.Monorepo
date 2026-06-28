using Customers.Host.Database;
using Customers.Host.Grpc.V1;
using FastEndpoints;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure;
using SharedKernel.Infrastructure.Behaviors;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.Messaging.DeadLetter;
using SharedKernel.Infrastructure.Observability;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
builder.AddTeckCloudObservability();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddCustomerPersistence();
builder.ConfigureInternalServiceTransport();
builder.AddHandlerServer();
builder.Host.UseWolverine(opts =>
{
    opts.AddTeckBehaviors();
    opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
});

var app = builder.Build();
app.UseTeckService();
app.MapHandlers(registry =>
    registry.Register<GetTenantDatabaseInfoCommand, GetTenantDatabaseInfoCommandHandler, TenantDatabaseInfoRpcResult>());
app.Run();
