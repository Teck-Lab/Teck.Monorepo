using System.Security.Claims;
using Billings.Application.Billing.Payments;
using Billings.Application.Billing.Purchases;
using Billings.Application.Database;
using Billings.Host.Database;
using Billings.Host.Infrastructure;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Hosting;
using Stripe;
using Teck.ServiceDefaults;
using Wolverine.Transports;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddBillingPersistence();
builder.Services.Configure<TenantPurchaseOptions>(builder.Configuration.GetSection(TenantPurchaseOptions.SectionName));
builder.Services.AddScoped<CreateTenantPurchaseHandler>();
builder.Services.AddScoped<CreateStripePurchaseIntentHandler>();
builder.Services.AddScoped<GetTenantPurchaseOrderHandler>();
builder.Services.AddScoped<ConfirmStripePurchaseHandler>();
builder.Services.AddScoped<ConfirmExternalInvoiceHandler>();
builder.Services.AddScoped<CancelTenantPurchaseHandler>();
builder.Services.AddScoped<SuspendExpiredPurchasesHandler>();
builder.Services.AddHostedService<SubscriptionExpiryService>();
builder.Services.AddSingleton(_ => new SubscriptionPaymentSigningKey(
    builder.Configuration["PaymentProof:EncryptedPrivateKey"], builder.Configuration["PaymentProof:Passphrase"]));
builder.Services.AddScoped<DeclineCategoryResolver>();
builder.Services.Configure<CustomerReservationOptions>(builder.Configuration.GetSection(CustomerReservationOptions.SectionName));
builder.Services.AddHttpClient<CustomerReservationClient>();
builder.Services.AddSingleton<PaymentIntentService>(_ =>
{
    var key = builder.Configuration["Stripe:SecretKey"];
    if (string.IsNullOrWhiteSpace(key))
    {
        throw new InvalidOperationException("Stripe:SecretKey must be supplied to process purchase payments.");
    }

    return new PaymentIntentService(new StripeClient(key));
});
builder.Services.AddScoped<IStripePurchaseGateway, StripePurchaseGateway>();
builder.Services.AddScoped<IPaymentProvider, StubPaymentProvider>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.Services.AddAuthorization(options => options.AddPolicy("PendingPurchaseBuyer", policy =>
    policy.RequireAuthenticatedUser().RequireClaim("aud", "billing-api")
        .RequireAssertion(context =>
            !string.IsNullOrWhiteSpace(context.User.FindFirst("sub")?.Value ??
                context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value) &&
            !context.User.HasClaim("billing_service_identity", "billing-api"))));
builder.AddTeckMessaging(typeof(BillingDbContext).Assembly, "BillingWrite", NamingSource.FromHandlerType);
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync<BillingDbContext>(args);
