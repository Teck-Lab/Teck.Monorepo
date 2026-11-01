using System.Net.Http.Json;
using Billings.Host.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace Billing.IntegrationTests;

public sealed class CustomerReservationClientTests
{
    [Fact]
    public async Task GetReservationAsync_RequestsClientCredentialsAndSendsServiceBearerToken()
    {
        using var handler = new ReservationHandler();
        using var http = new HttpClient(handler, disposeHandler: false);
        var client = new CustomerReservationClient(http, Options.Create(new CustomerReservationOptions
        {
            TokenEndpoint = "https://keycloak.test/realms/main/protocol/openid-connect/token",
            ClientId = "billing-api", ClientSecret = "secret", Scope = "tenant-purchase customer-api-audience",
            CustomerBaseUrl = "https://customer.test/",
        }));

        var response = await client.GetReservationAsync(Guid.Parse("7eb658d5-6bc9-4777-9dc3-0751261842dc"), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("acme-123", response.TenantIdentifier);
        Assert.Equal("service-owner", handler.BearerToken);
        Assert.Equal("7eb658d5-6bc9-4777-9dc3-0751261842dc", handler.RequestedReservationId);
        Assert.Equal("client_credentials", handler.GrantType);
        Assert.Equal("billing-api", handler.ClientId);
        Assert.Equal("tenant-purchase customer-api-audience", handler.Scope);
    }

    private sealed class ReservationHandler : HttpMessageHandler
    {
        public string? BearerToken { get; private set; }
        public string? GrantType { get; private set; }
        public string? ClientId { get; private set; }
        public string? Scope { get; private set; }
        public string? RequestedReservationId { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                GrantType = form["grant_type"];
                ClientId = form["client_id"];
                Scope = form["scope"];
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = JsonContent.Create(new { access_token = "service-owner" }) };
            }

            BearerToken = request.Headers.Authorization?.Parameter;
            RequestedReservationId = request.RequestUri.AbsolutePath.Split('/').Last();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = JsonContent.Create(new PurchaseReservationResponse { TenantId = Guid.NewGuid(), TenantIdentifier = "acme-123", OwnerSubjectId = "owner", Status = "pending", PurchaseOrderId = Guid.Empty }) };
        }
    }
}
