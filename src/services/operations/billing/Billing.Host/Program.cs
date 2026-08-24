using Billings.Application.Billing;
using Billings.Application.Billing.Payments;
using Billings.Application.Database;
using Billings.Host.Database;
using Billings.Host.Infrastructure;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Hosting;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddBillingPersistence();
builder.Services.Configure<PaymentProviderOptions>(builder.Configuration.GetSection(PaymentProviderOptions.SectionName));
builder.Services.AddScoped<DeclineCategoryResolver>();
builder.Services.AddScoped<IPaymentProvider, StubPaymentProvider>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.AddTeckMessaging(typeof(BillingDbContext).Assembly, "BillingWrite");
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);
