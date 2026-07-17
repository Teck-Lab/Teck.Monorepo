using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SharedKernel.Infrastructure.Behaviors;
using SharedKernel.Infrastructure.Database;
using SharedKernel.Infrastructure.Messaging;
using SharedKernel.Infrastructure.Messaging.DeadLetter;
using Wolverine;

namespace SharedKernel.Infrastructure.Hosting;

/// <summary>
/// Extension methods that attach the shared WolverineFx messaging runtime to a service host.
/// </summary>
public static class TeckMessagingExtensions
{
    /// <summary>
    /// Configures WolverineFx for the host, attaching the RabbitMQ transport only when a
    /// <c>rabbitmq</c> connection string is configured. When it is absent, the host falls back to
    /// local-only durable queues so standalone development can boot without a broker.
    /// </summary>
    /// <param name="builder">The web application builder to configure.</param>
    /// <param name="handlerAssembly">
    /// The assembly containing the service's command, query and event handlers. Wolverine only
    /// scans the entry assembly by default, so this must be included explicitly for handlers
    /// declared outside the host project to be discovered at runtime.
    /// </param>
    /// <param name="writeConnectionName">
    /// The connection-string name used to resolve the write database connection via
    /// <see cref="CodegenConnectionString.ResolveRequired"/>.
    /// </param>
    /// <returns>The same builder so calls can be chained.</returns>
    public static WebApplicationBuilder AddTeckMessaging(
        this WebApplicationBuilder builder,
        Assembly handlerAssembly,
        string writeConnectionName)
    {
        var write = CodegenConnectionString.ResolveRequired(builder.Configuration, writeConnectionName, "Default");
        var rabbit = builder.Configuration.GetConnectionString("rabbitmq");
        bool isDev = builder.Environment.IsDevelopment();

        builder.Host.UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(handlerAssembly);

            if (ShouldUseBroker(rabbit))
            {
                WolverinePersistenceConfigurator.ConfigureStandardRuntime(
                    opts,
                    isDev,
                    write,
                    WolverinePersistenceConfigurator.NormalizeRabbitConnectionString(rabbit!));
            }
            else
            {
                WolverinePersistenceConfigurator.ConfigureLocalOnlyRuntime(opts, isDev, write);
            }

            opts.AddTeckBehaviors();
            opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
        });

        return builder;
    }

    /// <summary>
    /// Decides whether the RabbitMQ transport should be attached, based on whether a non-blank
    /// <c>rabbitmq</c> connection string is configured. Extracted as a pure helper so the gating
    /// decision is unit-testable without standing up a host builder.
    /// </summary>
    /// <param name="rabbitConnectionString">The configured <c>rabbitmq</c> connection string, if any.</param>
    /// <returns><see langword="true"/> when the standard (broker-backed) runtime should be used.</returns>
    internal static bool ShouldUseBroker(string? rabbitConnectionString) =>
        !string.IsNullOrWhiteSpace(rabbitConnectionString);
}
