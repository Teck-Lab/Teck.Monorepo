var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
// Database resource names use a "db" suffix to avoid Aspire's case-insensitive name
// collision with the same-named project resources (order, customer, catalog).
var postgres = builder.AddPostgres("postgres").WithDataVolume();
var orderDb = postgres.AddDatabase("orderdb");
var customerDb = postgres.AddDatabase("customerdb");
var catalogDb = postgres.AddDatabase("catalogdb");

var rabbitmq = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();
var redis = builder.AddRedis("redis");

var keycloak = builder.AddKeycloak("keycloak")
    .WithDataVolume()
    .WithRealmImport("./realms");

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
// variables (services__customer__http__0, etc.) so YARP destinations like http://customer resolve.
var customer = builder.AddProject<Projects.Customer_Host>("customer")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__CustomerWrite", customerDb)
    .WithEnvironment("ConnectionStrings__CustomerRead", customerDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(customerDb);

// WithHttpEndpoint registers the "http" endpoint Aspire injects into service-discovery
// variables (services__catalog__http__0, etc.) so YARP destinations like http://catalog resolve.
var catalog = builder.AddProject<Projects.Catalog_Host>("catalog")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__CatalogWrite", catalogDb)
    .WithEnvironment("ConnectionStrings__CatalogRead", catalogDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(catalogDb).WaitFor(keycloak);

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
