using Microsoft.AspNetCore.Authentication;
using SharedKernel.Infrastructure.Auth;

namespace Gateway.Public.Edge.Steps;

/// <summary>Exchanges the inbound user token for a downstream audience token.</summary>
/// <param name="exchangeService">The token exchange service.</param>
public sealed class ExchangeTokenStep(IServiceTokenExchangeService exchangeService) : IEdgeStep
{
    private readonly IServiceTokenExchangeService exchangeService = exchangeService;

    /// <inheritdoc/>
    public async Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct)
    {
        string? audience = context.Policy.ExchangeAudience;
        if (string.IsNullOrWhiteSpace(audience))
        {
            return EdgeStepResult.Proceed; // anonymous route
        }

        string? inbound = ExtractBearer(context.HttpContext.Request.Headers.Authorization.ToString())
            ?? await context.HttpContext.GetTokenAsync("access_token").ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(inbound))
        {
            return EdgeStepResult.Proceed; // nothing to exchange (e.g. anonymous-but-tenant route)
        }

        try
        {
            ServiceTokenResult exchanged = await exchangeService
                .ExchangeTokenAsync(inbound, audience, context.ResolvedTenantId ?? "edge-no-tenant", ct)
                .ConfigureAwait(false);

            context.ExchangedToken = exchanged.AccessToken;
            context.HttpContext.Items[EdgeHeaders.ExchangedTokenItemKey] = exchanged.AccessToken;
            return EdgeStepResult.Proceed;
        }
        catch (TokenExchangeException ex) when (ex.IsAuthFailure)
        {
            int status = ex.StatusCode is 401 or 403 ? ex.StatusCode : 401;
            bool expired = status == 401 && (ex.Description?.Contains("expired", StringComparison.OrdinalIgnoreCase) ?? false);
            return EdgeStepResult.Stop(new EdgeProblem(
                status,
                status == 401 ? "Unauthorized" : "Forbidden",
                expired ? "Bearer token expired or invalid. Re-authenticate and try again."
                        : ex.Description ?? "Unable to exchange token for downstream access.",
                expired ? "authorization.token.expired" : "authorization.token_exchange_denied"));
        }
    }

    private static string? ExtractBearer(string? header) =>
        !string.IsNullOrWhiteSpace(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
}
