using System.Security.Cryptography;
using Licensing.Application;
using Licensing.Application.Database;
using Microsoft.Extensions.Options;

namespace Licensing.Host;

/// <summary>Builds issuer from secret-backed RSA key configuration.</summary>
public static class EnterprisePolicyRegistration
{
    /// <summary>Registers durable enterprise policy issuance and operator authorization.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddEnterprisePolicyIssuer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SharedKernel.Core.Database.IUnitOfWork, Database.LicensingPolicyUnitOfWork>();
        services.AddScoped<IEnterprisePolicyStore, EnterprisePolicyDbStore>();
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LicenseKeyOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.EncryptedPrivateKey) || string.IsNullOrWhiteSpace(options.Passphrase))
            {
                throw new InvalidOperationException("Enterprise policy signing key is not configured.");
            }

            var privateKey = RSA.Create();
            privateKey.ImportEncryptedPkcs8PrivateKey(options.Passphrase, Convert.FromBase64String(options.EncryptedPrivateKey), out _);
            return privateKey;
        });
        services.AddScoped<EnterprisePolicyIssuer>(sp => new EnterprisePolicyIssuer(sp.GetRequiredService<IEnterprisePolicyStore>(),
            sp.GetRequiredService<RSA>(), sp.GetRequiredService<IOptions<LicenseKeyOptions>>().Value.KeyId));
        services.AddAuthorization(options => options.AddPolicy("EnterprisePolicyIssuer", policy => policy.RequireAuthenticatedUser()
            .RequireClaim("aud", "licensing-api").RequireClaim("scope", "licensing:enterprise-policy:issue")
            .RequireRole("licensing-enterprise-policy-issuer").RequireClaim("sub")));
        return services;
    }
}
