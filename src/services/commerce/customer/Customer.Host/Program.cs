// Placeholder — Task 6 adds full host bootstrapping (Keycloak, WolverineFx, observability, etc.).
using Customers.Host.Database;

var builder = WebApplication.CreateBuilder(args);
builder.AddCustomerPersistence();
var app = builder.Build();
app.Run();
