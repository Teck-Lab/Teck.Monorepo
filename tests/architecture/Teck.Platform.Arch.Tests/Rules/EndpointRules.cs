using System.Reflection;
using FastEndpoints;
using SharedKernel.Infrastructure.Endpoints;
using Xunit;

namespace Teck.Platform.Arch.Tests.Rules;

/// <summary>Architecture rules for FastEndpoints endpoints in service Host assemblies.</summary>
public static class EndpointRules
{
    /// <summary>
    /// Every concrete FastEndpoints endpoint in a service Host must derive from
    /// <see cref="AuthenticatedEndpoint{TRequest,TResponse}"/>, so authorization wiring is
    /// declared once and cannot be bypassed with a raw <c>Endpoint&lt;,&gt;</c>.
    /// </summary>
    /// <param name="hostAssembly">The service Host assembly.</param>
    public static void EndpointsShouldDeriveFromAuthenticatedEndpoint(Assembly hostAssembly)
    {
        Type[] endpointTypes = hostAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IEndpoint).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(endpointTypes);

        Assert.All(endpointTypes, type =>
            Assert.True(
                DerivesFromAuthenticatedEndpoint(type),
                $"Endpoint '{type.FullName}' must derive from AuthenticatedEndpoint<,>."));
    }

    private static bool DerivesFromAuthenticatedEndpoint(Type type)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(AuthenticatedEndpoint<,>))
            {
                return true;
            }
        }

        return false;
    }
}
