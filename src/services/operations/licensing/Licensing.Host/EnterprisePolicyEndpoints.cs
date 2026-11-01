using Licensing.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Licensing.Host;

/// <summary>Operator-authorized enterprise policy issuance and retrieval endpoints.</summary>
/// <param name="issuer">The enterprise policy issuer.</param>
/// <param name="authorization">The authorization service.</param>
[ApiController]
[Route("enterprise-policies")]
public sealed class EnterprisePolicyEndpoints(EnterprisePolicyIssuer issuer, IAuthorizationService authorization) : ControllerBase
{
    /// <summary>Issues a signed policy for an authorized human operator.</summary>
    /// <param name="request">The policy terms to issue.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The newly issued policy or an authorization/validation response.</returns>
    [HttpPost]
    public async Task<IActionResult> Issue([FromBody] IssueEnterprisePolicyRequest request, CancellationToken cancellationToken)
    {
        if (!(await authorization.AuthorizeAsync(User, null, "EnterprisePolicyIssuer").ConfigureAwait(false)).Succeeded)
        {
            return Forbid();
        }

        var actor = new EnterprisePolicyOperator(User.FindFirst("sub")?.Value ?? string.Empty, User.FindFirst("aud")?.Value ?? string.Empty,
            User.FindAll("scope").SelectMany(x => x.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.Ordinal),
            User.FindAll("roles").Select(x => x.Value).ToHashSet(StringComparer.Ordinal));
        try
        {
            var issued = await issuer.IssueAsync(actor, request, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { policyId = issued.PolicyId, policyVersion = issued.PolicyVersion }, issued);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid enterprise policy", Detail = exception.Message });
        }
    }

    /// <summary>Retrieves an issued immutable enterprise policy version.</summary>
    /// <param name="policyId">The policy identifier.</param>
    /// <param name="policyVersion">The policy version.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The policy when found; otherwise not found.</returns>
    [HttpGet("{policyId}/{policyVersion}")]
    [Authorize(Policy = "EnterprisePolicyIssuer")]
    public async Task<IActionResult> Get(string policyId, string policyVersion, CancellationToken cancellationToken) =>
        await issuer.GetAsync(policyId, policyVersion, cancellationToken).ConfigureAwait(false) is { } policy ? Ok(policy) : NotFound();
}
