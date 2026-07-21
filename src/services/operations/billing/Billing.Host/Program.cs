using Billings.Application.Billing;
using Billings.Application.Billing.Payments;
using Billings.Application.Database;
using Billings.Host.Database;
using Billings.Host.Infrastructure;
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
builder.AddBillingPersistence();
builder.Services.Configure<PaymentProviderOptions>(builder.Configuration.GetSection(PaymentProviderOptions.SectionName));
builder.Services.AddScoped<IPaymentProvider, StubPaymentProvider>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.Host.UseWolverine(opts =>
{
    // Command, query and event handlers live in the Billings.Application assembly, but Wolverine
    // only scans the entry assembly (Billing.Host) by default. Include the application assembly so
    // handlers are discovered at runtime in every environment (not only in tests).
    opts.Discovery.IncludeAssembly(typeof(BillingDbContext).Assembly);
    opts.AddTeckBehaviors();
    opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
});
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);
