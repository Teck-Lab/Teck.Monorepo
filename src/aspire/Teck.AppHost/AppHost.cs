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
var notificationDb = postgres.AddDatabase("notificationdb");

var rabbitmq = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();
var redis = builder.AddRedis("redis");

var keycloak = builder.AddKeycloak("keycloak");
if (useVolumes)
{
    keycloak.WithDataVolume();
}

keycloak.WithRealmImport("./realms");

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
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(customerDb).WaitFor(keycloak);

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
builder.AddProject<Projects.Pricing_Host>("pricing")
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
    .WithEnvironment("ConnectionStrings__BillingWrite", billingDb)
    .WithEnvironment("ConnectionStrings__BillingRead", billingDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(billingDb).WaitFor(keycloak);

builder.AddProject<Projects.Notification_Host>("notification")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__NotificationWrite", notificationDb)
    .WithEnvironment("ConnectionStrings__NotificationRead", notificationDb)
    .WithReference(rabbitmq).WithReference(keycloak)
    .WaitFor(notificationDb).WaitFor(keycloak);

// WithHttpEndpoint registers a named "http" endpoint so Aspire can inject the correct
// ASPNETCORE_URLS and the testing framework can resolve it via CreateHttpClient("gateway", "http").
// Without this, project resources that have no launchSettings.json have no registered endpoint.
builder.AddProject<Projects.Gateway_Public>("gateway")
    .WithHttpEndpoint(name: "http")
    .WithReference(order).WithReference(customer).WithReference(catalog)
    .WithReference(keycloak).WithReference(redis)
    .WaitFor(order).WaitFor(customer).WaitFor(catalog)
    .WithExternalHttpEndpoints();

builder.Build().Run();
