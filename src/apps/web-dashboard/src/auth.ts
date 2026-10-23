import NextAuth, { type Session } from "next-auth";
import Keycloak from "next-auth/providers/keycloak";
import { getAuthEnvironment } from "./auth-config";

const authConfig = NextAuth(() => {
  const environment = getAuthEnvironment(process.env);
  return {
    providers: [
      Keycloak({
        issuer: environment.keycloakIssuer,
        clientId: environment.keycloakClientId,
        clientSecret: environment.keycloakClientSecret,
        checks: ["pkce", "state"],
        authorization: { params: { scope: "openid" } },
        profile(profile) {
          const realmAccess =
            "realm_access" in profile &&
            profile.realm_access &&
            typeof profile.realm_access === "object"
              ? profile.realm_access
              : undefined;
          const rawRoles = realmAccess && "roles" in realmAccess ? realmAccess.roles : undefined;
          const roles = Array.isArray(rawRoles)
            ? rawRoles.filter((role): role is string => typeof role === "string")
            : [];
          return {
            id: profile.sub,
            name: profile.name ?? profile.preferred_username ?? null,
            email: profile.email ?? null,
            roles,
          };
        },
      }),
    ],
    secret: environment.authSecret,
    trustHost: true,
    session: { strategy: "jwt", maxAge: 8 * 60 * 60 },
    callbacks: {
      async jwt({ token, account, user }) {
        if (account) {
          token.accessToken = account.access_token;
          token.refreshToken = account.refresh_token;
          token.accessTokenExpiresAt = account.expires_at
            ? account.expires_at * 1000
            : Date.now() + 60_000;
        }
        if (user) token.roles = user.roles;
        if (token.accessTokenExpiresAt && Date.now() < Number(token.accessTokenExpiresAt) - 30_000)
          return token;
        if (!token.refreshToken) return { ...token, error: "RefreshTokenMissing" };
        const response = await fetch(
          `${environment.keycloakIssuer}/protocol/openid-connect/token`,
          {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: new URLSearchParams({
              grant_type: "refresh_token",
              client_id: environment.keycloakClientId,
              client_secret: environment.keycloakClientSecret,
              refresh_token: String(token.refreshToken),
            }),
            cache: "no-store",
          },
        );
        if (!response.ok) return { ...token, error: "RefreshAccessTokenError" };
        const refreshed = await response.json();
        if (
          !refreshed ||
          typeof refreshed !== "object" ||
          !("access_token" in refreshed) ||
          typeof refreshed.access_token !== "string" ||
          !("expires_in" in refreshed) ||
          typeof refreshed.expires_in !== "number"
        ) {
          return { ...token, error: "RefreshAccessTokenError" };
        }
        const refreshToken =
          "refresh_token" in refreshed && typeof refreshed.refresh_token === "string"
            ? refreshed.refresh_token
            : token.refreshToken;
        return {
          ...token,
          accessToken: refreshed.access_token,
          refreshToken,
          accessTokenExpiresAt: Date.now() + refreshed.expires_in * 1000,
          error: undefined,
        };
      },
      async session({ session, token }) {
        if (session.user) {
          session.user.roles = Array.isArray(token.roles)
            ? token.roles.filter((role): role is string => typeof role === "string")
            : [];
        }
        return session;
      },
    },
  };
});

export const { handlers, signIn, signOut } = authConfig;
export const auth: (args?: unknown) => Promise<Session | null> = authConfig.auth;
