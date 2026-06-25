using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Core.Options;

namespace SharedKernel.Infrastructure.Options;

/// <summary>
/// The extensions.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Load the options.
    /// </summary>
    /// <typeparam name="T">The options type to load and bind.</typeparam>
    /// <param name="configuration">The configuration.</param>
    /// <param name="sectionName">The section name.</param>
    /// <exception cref="ConfigurationMissingException">.</exception>
    /// <returns>A <typeparamref name="T"/>.</returns>
    [RequiresDynamicCode("Binding strongly typed objects to configuration values may require generating dynamic code at runtime.")]
    [RequiresUnreferencedCode("Binding strongly typed objects to configuration values may require generating dynamic code at runtime.")]
    public static T LoadOptions<T>(this IConfiguration configuration, string sectionName)
        where T : IOptionsRoot
    {
        T options = configuration.GetSection(sectionName).Get<T>() ?? throw new ConfigurationMissingException(sectionName);
        return options;
    }

    /// <summary>
    /// Bind validate return.
    /// </summary>
    /// <typeparam name="T">The options type to bind, validate and return.</typeparam>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>A <typeparamref name="T"/>.</returns>
    [RequiresDynamicCode("Binding strongly typed objects to configuration values may require generating dynamic code at runtime.")]
    [RequiresUnreferencedCode("Binding strongly typed objects to configuration values may require generating dynamic code at runtime.")]
    public static T BindValidateReturn<T>(this IServiceCollection services, IConfiguration configuration)
        where T : class, IOptionsRoot
    {
        services.AddOptions<T>()
            .BindConfiguration(typeof(T).Name)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return configuration.LoadOptions<T>(typeof(T).Name);
    }

    /// <summary>
    /// Bind the validate.
    /// </summary>
    /// <typeparam name="T">The options type to bind and validate.</typeparam>
    /// <param name="services">The services.</param>
    [RequiresDynamicCode("Binding strongly typed objects to configuration values may require generating dynamic code at runtime.")]
    public static void BindValidate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(this IServiceCollection services)
        where T : class, IOptionsRoot
    {
        services.AddOptions<T>()
            .BindConfiguration(typeof(T).Name)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
