using Finbuckle.MultiTenant.Abstractions;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using Wolverine;

namespace Pricing.Application.Pricing.Features.CreatePriceList.V1;

/// <summary>Handles <see cref="CreatePriceListCommand"/>.</summary>
public static class CreatePriceListHandler
{
    /// <summary>Creates a draft price list and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="bus">The message bus (unused for draft; kept for signature symmetry).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created price list.</returns>
    public static async Task<PriceListDto> Handle(
        CreatePriceListCommand command,
        IGenericWriteRepository<PriceList, Guid> repository,
        IUnitOfWork unitOfWork,
        ITenantInfo tenant,
        IMessageBus bus,
        CancellationToken ct)
    {
        var scope = new PriceScope(command.Currency, command.Country, command.CustomerGroupId, command.ChannelId);
        var list = PriceList.Create(command.Name, scope, command.ValidFrom, command.ValidUntil, tenant.Id ?? string.Empty);
        list.UpdateDetails(command.Name, command.Description);

        await repository.AddAsync(list, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return list.ToDto();
    }
}
