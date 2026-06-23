namespace SharedKernel.Infrastructure.Messaging.Idempotency;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class IdempotentAttribute : Attribute
{
    public int TtlHours { get; init; } = 24;
}
