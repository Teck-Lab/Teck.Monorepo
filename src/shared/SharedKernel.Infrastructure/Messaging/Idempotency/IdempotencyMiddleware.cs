using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Wolverine;

namespace SharedKernel.Infrastructure.Messaging.Idempotency;

/// <summary>
/// Wolverine middleware that suppresses duplicate processing of messages marked with
/// <see cref="IdempotentAttribute"/> by recording a Redis-backed idempotency key.
/// </summary>
/// <param name="logger">The logger used to record idempotency decisions and key handling.</param>
/// <param name="database">The Redis database used to store and check idempotency keys.</param>
public sealed class IdempotencyMiddleware(ILogger<IdempotencyMiddleware> logger, IDatabase database)
{
    /// <summary>
    /// Executes before the handler, short-circuiting the pipeline when the incoming message is a duplicate
    /// of one already processed within the configured idempotency window.
    /// </summary>
    /// <param name="context">The Wolverine message context for the current envelope.</param>
    /// <param name="next">The delegate that continues the message handling pipeline.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    public async ValueTask BeforeAsync(
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
