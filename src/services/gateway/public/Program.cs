var builder = WebApplication.CreateBuilder(args);
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();
var app = builder.Build();
app.UseRouting();
app.MapReverseProxy();
app.Run();

/// <summary>
/// Entry point for the public-gateway host; exposed as a partial class so
/// integration tests can reference it via <c>WebApplicationFactory</c>.
/// </summary>
public partial class Program { }
