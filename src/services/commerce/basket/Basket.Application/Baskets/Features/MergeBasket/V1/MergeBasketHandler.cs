using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.MergeBasket.V1;

/// <summary>Handles <see cref="MergeBasketCommand"/>.</summary>
public static class MergeBasketHandler
{
    /// <summary>Merges the guest basket into the customer's active basket (creating it if needed) and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="identity">The identity accessor (must have a customer).</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The customer's merged basket.</returns>
    public static async Task<BasketDto> Handle(
        MergeBasketCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IUnitOfWork unitOfWork,
        IBasketIdentityAccessor identity,
        ITenantInfo tenant,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identity.Subject))
        {
            throw new InvalidOperationException("Merge requires an authenticated customer.");
        }

        var target = await repository.FirstOrDefaultAsync(new ActiveBasketBySubjectSpec(identity.Subject), enableTracking: true, ct).ConfigureAwait(false);
        if (target is null)
        {
            target = Basket.CreateForSubject(identity.Subject, tenant.Id ?? string.Empty);
            await repository.AddAsync(target, ct).ConfigureAwait(false);
        }

        var source = await repository.FirstOrDefaultAsync(new ActiveBasketByTokenSpec(command.AnonymousToken), enableTracking: true, ct).ConfigureAwait(false);
        if (source is not null)
        {
            target.MergeFrom(source);
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return BasketMapper.ToDto(target);
    }
}
