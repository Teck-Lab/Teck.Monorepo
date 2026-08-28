using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Teck.LocalIdentity;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();
var options = new LocalIdentityOptions
{
    BaseUrl = configuration["Keycloak:BaseUrl"] ?? "http://localhost:8080",
    AdminUsername = configuration["Keycloak:AdminUsername"] ?? "admin",
    AdminPassword = configuration["Keycloak:AdminPassword"] ?? string.Empty,
    ManifestPath = configuration["Keycloak:ManifestPath"] ?? "src/aspire/Teck.AppHost/realms/teck-realm.json",
    OrganizationManifestPath = configuration["Keycloak:OrganizationManifestPath"] ?? "src/aspire/Teck.AppHost/identity/local-organizations.json",
    Realm = configuration["Keycloak:Realm"] ?? "teck",
};
options.Validate();

string manifestPath = FindManifest(options.ManifestPath);
using var httpClient = new HttpClient { BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute) };
using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false));
await new RealmReconciler(httpClient, options).ReconcileAsync(manifest, CancellationToken.None).ConfigureAwait(false);

string organizationManifestPath = FindManifest(options.OrganizationManifestPath);
using JsonDocument organizationManifest = JsonDocument.Parse(await File.ReadAllTextAsync(organizationManifestPath).ConfigureAwait(false));
string customerWriteConnectionString = configuration.GetConnectionString("CustomerWrite") ?? throw new InvalidOperationException("ConnectionStrings__CustomerWrite must be configured.");
var organizationReconciler = new OrganizationReconciler(httpClient, options);
var tenantRegistryWriter = TenantRegistryWriter.Create(customerWriteConnectionString);
IReadOnlyList<ProvisionedOrganization> organizations = await new LocalIdentityProvisioner(organizationReconciler, tenantRegistryWriter)
    .ProvisionAsync(organizationManifest, CancellationToken.None)
    .ConfigureAwait(false);

Console.WriteLine("Organization ID\tTenant identifier");
foreach (ProvisionedOrganization organization in organizations)
{
    Console.WriteLine($"{organization.Id}\t{organization.Alias}");
}

static string FindManifest(string configuredPath)
{
    foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.GetFullPath(Path.Combine(directory.FullName, configuredPath));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    throw new FileNotFoundException($"The local Keycloak realm manifest '{configuredPath}' was not found.");
}
