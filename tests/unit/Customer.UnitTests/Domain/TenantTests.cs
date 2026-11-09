using Customers.Domain.Entities;
using Xunit;

namespace Customer.UnitTests.Domain;

public sealed class TenantTests
{
    [Fact]
    public void Create_SetsProvidedValues()
    {
        var id = Guid.NewGuid();
        var tenant = Tenant.Create(id, "acme", "shared", "postgres", hasReadReplicas: false);

        Assert.Equal(id, tenant.Id);
        Assert.Equal("acme", tenant.Identifier);
        Assert.Equal("shared", tenant.DatabaseStrategy);
        Assert.Equal("postgres", tenant.DatabaseProvider);
        Assert.False(tenant.HasReadReplicas);
    }

    [Fact]
    public void CreatePendingReservation_BindsOwnerAndStaysPendingWithoutPurchaseClaims()
    {
        var id = Guid.NewGuid();
        var tenant = Tenant.CreatePendingReservation(id, "acme-business-1234", "owner-subject");

        Assert.Equal(id, tenant.Id);
        Assert.Equal("pending", tenant.Status);
        Assert.Equal("owner-subject", tenant.OwnerSubjectId);
        Assert.Equal(string.Empty, tenant.PlanId);
        Assert.Equal(Guid.Empty, tenant.PurchaseOrderId);
        Assert.Equal(string.Empty, tenant.DatabaseStrategy);
        Assert.Equal(Guid.Empty, tenant.PurchaseOrderId);
    }

    [Fact]
    public void CreatePendingReservation_RejectsMissingOwner()
    {
        Assert.Throws<ArgumentException>(() => Tenant.CreatePendingReservation(Guid.NewGuid(), "acme", " "));
    }

    [Fact]
    public void ApplyPurchaseConfirmation_LeavesTenantInactiveUntilBothProvisionersAreReady()
    {
        var pending = Tenant.CreatePendingReservation(Guid.NewGuid(), "acme", "owner");
        pending.ApplyPurchaseConfirmation(Guid.NewGuid(), Guid.NewGuid(), "pro", "2026-10", "dedicated", "postgres", false, DateTimeOffset.UtcNow.AddYears(1));

        Assert.Equal("paid_pending_readiness", pending.Status);
        pending.MarkProvisionerReady("license");
        Assert.Equal("paid_pending_readiness", pending.Status);
        pending.MarkProvisionerReady("database");
        Assert.Equal("paid_pending_readiness", pending.Status);
        pending.LinkOrganization(Guid.NewGuid());
        Assert.Equal("active", pending.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_RejectsEmptyIdentifier(string? identifier)
    {
        Assert.Throws<ArgumentException>(() => Tenant.Create(Guid.NewGuid(), identifier!, "shared", "postgres", false));
    }
}
