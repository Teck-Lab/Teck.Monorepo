export interface AuthEnvironment {
  keycloakIssuer: string;
  keycloakClientId: string;
  keycloakClientSecret: string;
  authSecret: string;
}

export function getAuthEnvironment(environment: NodeJS.ProcessEnv): AuthEnvironment {
  const required = (key: string): string => {
    const value = environment[key];
    if (!value?.trim()) throw new Error(`${key} is required`);
    return value;
  };
  return {
    keycloakIssuer: required("KEYCLOAK_ISSUER"),
    keycloakClientId: required("KEYCLOAK_CLIENT_ID"),
    keycloakClientSecret: required("KEYCLOAK_CLIENT_SECRET"),
    authSecret: required("AUTH_SECRET"),
  };
}
