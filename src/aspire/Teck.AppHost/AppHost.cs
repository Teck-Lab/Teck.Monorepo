var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
// Database resource names use a "db" suffix to avoid Aspire's case-insensitive name
// collision with the same-named project resources (order, customer, catalog).
// A generated password changes between AppHost launches, which cannot authenticate against a
// retained PostgreSQL data volume. Keep the password in the AppHost secret store instead.
var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var postgres = builder.AddPostgres("postgres", password: postgresPassword);
bool useVolumes = !string.Equals(builder.Configuration["UseVolumes"], "false", StringComparison.OrdinalIgnoreCase);
if (useVolumes)
{
    postgres.WithDataVolume();
}

var orderDb = postgres.AddDatabase("orderdb");
var basketDb = postgres.AddDatabase("basketdb");
var customerDb = postgres.AddDatabase("customerdb");
var catalogDb = postgres.AddDatabase("catalogdb");
var inventoryDb = postgres.AddDatabase("inventorydb");
var pricingDb = postgres.AddDatabase("pricingdb");
var billingDb = postgres.AddDatabase("billingdb");
var licensingDb = postgres.AddDatabase("licensingdb");
var notificationDb = postgres.AddDatabase("notificationdb");

var rabbitmq = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();
var redis = builder.AddRedis("redis");

// A generated Keycloak password changes between AppHost launches and cannot authenticate
// against a retained Keycloak data volume. Keep this local-only secret in the AppHost store.
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
var billingClientSecret = builder.AddParameter("billing-client-secret", secret: true);
var customerOrganizationClientSecret = builder.AddParameter("customer-org-provisioner-secret", secret: true);
var stripeSecretKey = builder.AddParameter("stripe-secret-key", secret: true);
var stripeWebhookSecret = builder.AddParameter("stripe-webhook-secret", secret: true);
var billingPaymentPrivateKey = builder.AddParameter("billing-payment-private-key", secret: true);
var billingPaymentPublicKey = builder.AddParameter("billing-payment-public-key", secret: true);
var billingPaymentPassphrase = builder.AddParameter("billing-payment-passphrase", secret: true);
var licensePrivateKey = builder.AddParameter("license-private-key", secret: true);
var licensePublicKey = builder.AddParameter("license-public-key", secret: true);
var licensePassphrase = builder.AddParameter("license-passphrase", secret: true);
var licenseReadinessKey = builder.AddParameter("license-readiness-key", secret: true);
var databaseReadinessKey = builder.AddParameter("database-readiness-key", secret: true);
bool useFixedKeycloakPort = !string.Equals(builder.Configuration["UseFixedKeycloakPort"], "false", StringComparison.OrdinalIgnoreCase);
// The developer command keeps localhost:8080 for the committed Development settings. AppHost
// integration tests opt out so each DCP instance receives an isolated dynamic host port.
var keycloak = builder.AddKeycloak("keycloak", port: useFixedKeycloakPort ? 8080 : null, adminPassword: keycloakAdminPassword)
    .WithImageTag("26.8.1");
if (useVolumes)
{
    keycloak.WithDataVolume();
}

string keycloakRealmImportPath = builder.Configuration["KeycloakRealmImportPath"] ?? "./realms";
keycloak.WithRealmImport(keycloakRealmImportPath);

// Services. ConnectionStrings__{Name} env vars match what each persistence extension reads
// (OrderWrite/OrderRead/CustomerWrite/CustomerRead/CatalogWrite/CatalogRead);
// redis + rabbitmq references inject their own connection names for future consumers.
// WithHttpEndpoint registers the "http" endpoint Aspire injects into service-discovery
// variables (services__order__http__0, etc.) so YARP destinations like http://order resolve.
var order = builder.AddProject<Projects.Order_Host>("order")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__OrderWrite", orderDb)
    .WithEnvironment("ConnectionStrings__OrderRead", orderDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(orderDb).WaitFor(keycloak);

// WithHttpEndpoint registers the "http" endpoint Aspire injects into service-discovery
// variables (services__basket__http__0, etc.) so YARP destinations like http://basket resolve.
// basket publishes BasketCheckedOut over rabbitmq, which order consumes to create an order.
builder.AddProject<Projects.Basket_Host>("basket")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__BasketWrite", basketDb)
    .WithEnvironment("ConnectionStrings__BasketRead", basketDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(basketDb).WaitFor(keycloak);

// WithHttpEndpoint registers the "http" endpoint Aspire injects into service-discovery
// variables (services__inventory__http__0, etc.) so YARP destinations like http://inventory resolve.
// inventory consumes BasketCheckedOut/OrderPlaced over rabbitmq; kept out of the gateway's
// WaitFor chain so the gateway smoke test's startup criteria stay independent of inventory.
builder.AddProject<Projects.Inventory_Host>("inventory")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__InventoryWrite", inventoryDb)
    .WithEnvironment("ConnectionStrings__InventoryRead", inventoryDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(inventoryDb).WaitFor(keycloak);

// WithHttpEndpoint registers the "http" endpoint Aspire injects into service-discovery
// variables (services__customer__http__0, etc.) so YARP destinations like http://customer resolve.
var customer = builder.AddProject<Projects.Customer_Host>("customer")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__CustomerWrite", customerDb)
    .WithEnvironment("ConnectionStrings__CustomerRead", customerDb)
    .WithEnvironment("KeycloakOrganizations__ServerUrl", keycloak.GetEndpoint("http"))
    .WithEnvironment("KeycloakOrganizations__Realm", "teck")
    .WithEnvironment("KeycloakOrganizations__AdminClientId", "customer-org-provisioner")
    .WithEnvironment("KeycloakOrganizations__AdminClientSecret", customerOrganizationClientSecret)
    .WithEnvironment("Readiness__LicenseKey", licenseReadinessKey)
    .WithEnvironment("Readiness__DatabaseKey", databaseReadinessKey)
    .WithEnvironment("PaymentProof__TrustedPublicKeys__0", billingPaymentPublicKey)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(customerDb).WaitFor(keycloak);

builder.AddProject<Projects.Licensing_Host>("licensing")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__LicensingWrite", licensingDb)
    .WithEnvironment("LicenseSigning__KeyId", "local-signing-key")
    .WithEnvironment("LicenseSigning__EncryptedPrivateKey", licensePrivateKey)
    .WithEnvironment("LicenseSigning__Passphrase", licensePassphrase)
    .WithEnvironment("LicenseSigning__TrustedPublicKeys__0", licensePublicKey)
    .WithEnvironment("Readiness__LicenseKey", licenseReadinessKey)
    .WithEnvironment("PaymentProof__TrustedPublicKeys__0", billingPaymentPublicKey)
    .WithReference(rabbitmq)
    .WaitFor(licensingDb);

// Local-only tooling reconciles the committed Keycloak manifest after Keycloak and Customer
// are available. Organization membership and tenant-registry writes are deliberately added by M4.
builder.AddProject<Projects.Teck_LocalIdentity>("local-identity")
    .WithEnvironment("Keycloak__BaseUrl", keycloak.GetEndpoint("http"))
    .WithEnvironment("Keycloak__AdminUsername", "admin")
    .WithEnvironment("Keycloak__AdminPassword", keycloakAdminPassword)
    .WithEnvironment("ConnectionStrings__CustomerWrite", customerDb)
    .WaitFor(keycloak).WaitFor(customer);

// WithHttpEndpoint registers the "http" endpoint Aspire injects into service-discovery
// variables (services__catalog__http__0, etc.) so YARP destinations like http://catalog resolve.
var catalog = builder.AddProject<Projects.Catalog_Host>("catalog")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__CatalogWrite", catalogDb)
    .WithEnvironment("ConnectionStrings__CatalogRead", catalogDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(catalogDb).WaitFor(keycloak);

// WithHttpEndpoint registers the "http" endpoint Aspire injects into service-discovery
// variables (services__pricing__http__0, etc.) so YARP destinations like http://pricing resolve.
// pricing resolves product list prices with multi-currency FX; it emits PriceChanged for
// future consumers and consumes nothing.
var pricing = builder.AddProject<Projects.Pricing_Host>("pricing")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__PricingWrite", pricingDb)
    .WithEnvironment("ConnectionStrings__PricingRead", pricingDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(pricingDb).WaitFor(keycloak);

// WithHttpEndpoint registers the "http" endpoint Aspire injects into service-discovery
// variables (services__billing__http__0, etc.) so YARP destinations like http://billing resolve.
// billing consumes OrderPlaced over rabbitmq; kept out of the gateway's WaitFor chain so the
// gateway smoke test's startup criteria stay independent of billing.
builder.AddProject<Projects.Billing_Host>("billing")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__BillingRead", billingDb)
    .WithEnvironment("CustomerReservations__CustomerBaseUrl", "http://customer")
    .WithEnvironment("CustomerReservations__TokenEndpoint", "http://keycloak:8080/realms/teck/protocol/openid-connect/token")
    .WithEnvironment("CustomerReservations__ClientId", "billing-api")
    .WithEnvironment("CustomerReservations__ClientSecret", billingClientSecret)
    .WithEnvironment("CustomerReservations__Scope", "tenant-purchase customer-api-audience")
    .WithEnvironment("Stripe__SecretKey", stripeSecretKey)
    .WithEnvironment("Stripe__WebhookSecret", stripeWebhookSecret)
    .WithEnvironment("PaymentProof__EncryptedPrivateKey", billingPaymentPrivateKey)
    .WithEnvironment("PaymentProof__Passphrase", billingPaymentPassphrase)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak).WithReference(customer)
    .WaitFor(billingDb).WaitFor(keycloak).WaitFor(customer);

builder.AddProject<Projects.Notification_Host>("notification")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__NotificationWrite", notificationDb)
    .WithEnvironment("ConnectionStrings__NotificationRead", notificationDb)
    .WithReference(rabbitmq).WithReference(keycloak)
    .WaitFor(notificationDb).WaitFor(keycloak);

// Named HTTP endpoints expose each gateway's dynamic port to Aspire and its tests.
// Customer traffic reaches only shared Catalog reads; admin traffic reaches those reads
// and the operator Catalog/Pricing routes. Customer supplies tenant authority to both.
builder.AddProject<Projects.Gateway_WebCustomer>("web-customer")
    .WithHttpEndpoint(name: "http")
    .WithReference(catalog).WithReference(customer).WithReference(keycloak).WithReference(redis)
    .WaitFor(catalog).WaitFor(customer).WaitFor(keycloak)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Gateway_WebAdmin>("web-admin")
    .WithHttpEndpoint(name: "http")
    .WithReference(catalog).WithReference(pricing).WithReference(customer)
    .WithReference(keycloak).WithReference(redis)
    .WaitFor(catalog).WaitFor(pricing).WaitFor(customer).WaitFor(keycloak)
    .WithExternalHttpEndpoints();

builder.Build().Run();
