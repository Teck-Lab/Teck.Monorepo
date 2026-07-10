using Pricing.Application.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Pricing.Host.Database;

/// <summary>Pricing write repository bound to <see cref="PricingDbContext"/>.</summary>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TId">The entity identifier type.</typeparam>
/// <param name="dbContext">The pricing write context.</param>
/// <param name="httpContextAccessor">The HTTP context accessor used for audit stamping on bulk deletes.</param>
public sealed class PricingWriteRepository<TEntity, TId>(PricingDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    : GenericWriteRepository<TEntity, TId, PricingDbContext>(dbContext, httpContextAccessor)
    where TEntity : BaseEntity;
