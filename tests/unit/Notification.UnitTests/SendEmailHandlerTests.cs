using Ardalis.Specification;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Notifications.Application.Notifications;
using Notifications.Application.Notifications.Features.SendEmail.V1;
using Notifications.Application.Notifications.ReadModels;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Notifications.UnitTests;

public sealed class SendEmailHandlerTests
{
    [Fact]
    public async Task Handle_NonRaceDbUpdateException_MarksRetryableAndRethrows()
    {
        var delivery = CreateDelivery("non-race-key");
        var deliveries = Substitute.For<IGenericWriteRepository<NotificationDelivery, Guid>>();
        deliveries.FirstOrDefaultAsync(Arg.Any<ISpecification<NotificationDelivery>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<NotificationDelivery?>(delivery));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var failure = new DbUpdateException("injected non-race failure");
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(failure), Task.FromResult(1));
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => SendEmailHandler.Handle(new SendEmailCommand(delivery.Id), deliveries, unitOfWork, sender, CancellationToken.None));

        Assert.Same(failure, thrown);
        Assert.Equal(DeliveryStatus.Retryable, delivery.Status);
        await unitOfWork.Received(1).RollbackTransactionAsync(CancellationToken.None);
        await unitOfWork.Received(2).SaveChangesAsync(CancellationToken.None);
        await sender.DidNotReceive().HasAcceptedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReceiptUniqueRaceWithFreshSentWinner_ReturnsSuccessfully()
    {
        var delivery = CreateDelivery("race-key");
        var winner = CreateDelivery("race-key");
        winner.MarkSent(DateTimeOffset.UtcNow);
        var deliveries = Substitute.For<IGenericWriteRepository<NotificationDelivery, Guid>>();
        deliveries.FirstOrDefaultAsync(Arg.Any<ISpecification<NotificationDelivery>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<NotificationDelivery?>(delivery));
        deliveries.FirstOrDefaultAsync(Arg.Any<ISpecification<NotificationDelivery>>(), false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<NotificationDelivery?>(winner));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(CreateReceiptUniqueViolation()));
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        sender.HasAcceptedAsync(delivery.TenantId, delivery.IdempotencyKey, CancellationToken.None)
            .Returns(Task.FromResult(true));

        await SendEmailHandler.Handle(new SendEmailCommand(delivery.Id), deliveries, unitOfWork, sender, CancellationToken.None);

        await unitOfWork.Received(1).RollbackTransactionAsync(CancellationToken.None);
        await sender.Received(1).HasAcceptedAsync(delivery.TenantId, delivery.IdempotencyKey, CancellationToken.None);
        await deliveries.Received(1).FirstOrDefaultAsync(Arg.Any<DeliveryByIdSpec>(), false, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_OtherPostgreSqlUniqueViolation_MarksRetryableAndRethrows()
    {
        var delivery = CreateDelivery("other-index-key");
        var deliveries = Substitute.For<IGenericWriteRepository<NotificationDelivery, Guid>>();
        deliveries.FirstOrDefaultAsync(Arg.Any<ISpecification<NotificationDelivery>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<NotificationDelivery?>(delivery));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var failure = new DbUpdateException("injected other unique failure", CreateUniqueViolation("IX_notification_deliveries_TenantId_IdempotencyKey"));
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(failure), Task.FromResult(1));
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => SendEmailHandler.Handle(new SendEmailCommand(delivery.Id), deliveries, unitOfWork, sender, CancellationToken.None));

        Assert.Same(failure, thrown);
        Assert.Equal(DeliveryStatus.Retryable, delivery.Status);
        await sender.DidNotReceive().HasAcceptedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static NotificationDelivery CreateDelivery(string idempotencyKey) =>
        NotificationDelivery.Create("tenant-a", Guid.NewGuid(), Guid.NewGuid(), "subject-a", idempotencyKey, $"source:{idempotencyKey}", NotificationKind.OrderConfirmed, "Your order is confirmed", "Your order is confirmed.", "shopper@example.test", null);

    private static DbUpdateException CreateReceiptUniqueViolation() =>
        new("receipt acceptance race", CreateUniqueViolation("IX_stub_email_acceptances_TenantId_IdempotencyKey"));

    private static PostgresException CreateUniqueViolation(string constraintName) =>
        new("duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation, null, null, 0, 0, null, null, null, null, null, null, constraintName, null, null, null);
}
