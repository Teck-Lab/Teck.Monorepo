using Keycloak.AuthServices.Authentication;
using SharedKernel.Core.Options;

namespace SharedKernel.Infrastructure.Auth;

/// <summary>
/// The keycloak options.
/// </summary>
public class KeycloakOptions : KeycloakAuthenticationOptions, IOptionsRoot
{ }
