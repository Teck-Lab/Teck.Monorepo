using System.Security.Claims;
using Customers.Application.Database;
using Customers.Domain.Entities;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.OpenApi;

namespace Customers.Host.Endpoints.Tenants;

/// <summary>Reserves a pending tenant identifier for the authenticated owner.</summary>
/// <param name="context">The global Customer registry context.</param>
public sealed class CreateTenantOnboardingEndpoint(CustomerDbContext context)
    : Endpoint<TenantOnboardingRequest, TenantOnboardingDto>
{
    /// <inheritdoc/>
    public override void Configure()
    {
        Post("/tenants/onboarding");
        AuthSchemes("Bearer");
        Policies("PendingTenantOnboarding");
        Metadata(new OpenApiAudienceMetadata("public"));
        Version(0);
    }

    /// <inheritdoc/>
    public override async Task HandleAsync(TenantOnboardingRequest request, CancellationToken ct)
    {
        var owner = HttpContext.User.FindFirst("sub")?.Value ?? HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var slug = Slugify(request.DisplayName);
        if (slug.Length == 0)
        {
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var identifier = $"{slug}-{owner[..Math.Min(owner.Length, 8)]}".ToLowerInvariant();
        var existing = await context.Tenants.SingleOrDefaultAsync(item => item.Identifier == identifier, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.OwnerSubjectId, owner, StringComparison.Ordinal))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await Send.OkAsync(new TenantOnboardingDto(existing.Id, existing.Identifier, existing.Status, existing.OwnerSubjectId), ct);
            return;
        }

        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), identifier, owner);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        HttpContext.Response.Headers.Location = $"/tenants/{tenant.Id}";
        await Send.ResponseAsync(new TenantOnboardingDto(tenant.Id, tenant.Identifier, tenant.Status, tenant.OwnerSubjectId), StatusCodes.Status201Created, ct);
    }

    private static string Slugify(string value)
    {
        var characters = value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray();
        return new string(characters).Trim('-');
    }
}
