using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Wolverine;

namespace SharedKernel.Infrastructure.Messaging.Idempotency;

public sealed class IdempotencyMiddleware(ILogger<IdempotencyMiddleware> logger, IDatabase database)
{
    public async ValueTask InvokeAsync(
        IMessageContext context,
        Func<ValueTask> next,
        CancellationToken cancellationToken)
    {
        object? message = context.Envelope?.Message;
        if (message is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        Type messageType = message.GetType();
        IdempotentAttribute? idempotentAttribute = messageType.GetCustomAttribute<IdempotentAttribute>(inherit: false);

        if (idempotentAttribute is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        string payload = JsonSerializer.Serialize(message, messageType);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        string cacheKey = $"idempotent:{messageType.FullName ?? messageType.Name}:{hash}";
        TimeSpan ttl = TimeSpan.FromHours(idempotentAttribute.TtlHours);

        bool acquired = await database.StringSetAsync(cacheKey, "1", ttl, when: When.NotExists).ConfigureAwait(false);
        if (!acquired)
        {
            logger.LogWarning("Skipping duplicate idempotent message {MessageType} with key {IdempotencyKey}", messageType.FullName ?? messageType.Name, cacheKey);
            return;
        }

        await next().ConfigureAwait(false);
    }
}
