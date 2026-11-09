using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;
using Teck.Platform.IntegrationTests.Shared;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Proves that physical recovery is not accepted from CNPG metadata without querying all five databases.</summary>
[Collection("SharedTestcontainers")]
public sealed class TenantRecoveredDatabaseInspectorTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task InspectAsync_RequiresEveryRecoveredTenantDatabaseToAnswerPostgres()
    {
        var tenantId = Guid.NewGuid();
        var names = TenantDatabaseReconciler.SupportedServices.Select(service => $"tenant_{tenantId:N}_{service}").ToArray();
        await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
        await admin.OpenAsync();
        try
        {
            foreach (string name in names.Take(4))
            {
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            var creds = new NpgsqlConnectionStringBuilder(fixture.AdminConnectionString);
            using var http = new HttpClient(new SuperuserSecretHandler(creds.Username!, creds.Password!))
            {
                BaseAddress = new Uri("https://kubernetes.test/"),
            };
            var inspector = new NpgsqlTenantRecoveredDatabaseInspector(http, _ => creds.Host!, creds.Port,
                SslMode.Disable, null);
            Assert.False(await inspector.InspectAsync($"restore-{tenantId:N}", tenantId, CancellationToken.None));

            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{names[4]}\"", admin))
                await create.ExecuteNonQueryAsync();
            Assert.True(await inspector.InspectAsync($"restore-{tenantId:N}", tenantId, CancellationToken.None));
        }
        finally
        {
            foreach (string name in names)
            {
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }

    private sealed class SuperuserSecretHandler(string username, string password) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("-superuser", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var body = new { type = "kubernetes.io/basic-auth", data = new
            {
                username = Convert.ToBase64String(Encoding.UTF8.GetBytes(username)),
                password = Convert.ToBase64String(Encoding.UTF8.GetBytes(password)),
            } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body)),
            });
        }
    }
}
