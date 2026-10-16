import type { NextAuthOptions } from "next-auth";
import KeycloakProvider from "next-auth/providers/keycloak";

const clientSecret = process.env.KEYCLOAK_CLIENT_SECRET ?? "";
if (
  !clientSecret &&
  process.env.NODE_ENV === "production" &&
  process.env.NEXT_PHASE !== "phase-production-build"
) {
  throw new Error("KEYCLOAK_CLIENT_SECRET must be configured before serving the dashboard.");
}

export async function refreshAccessToken(token: Record<string, unknown>, refresh = fetch) {
  const expiresAt = token.accessTokenExpiresAt;
  if (
    typeof token.accessToken === "string" &&
    typeof expiresAt === "number" &&
    Date.now() < expiresAt * 1000 - 30_000
  ) {
    return token;
  }
  if (typeof token.refreshToken !== "string") {
    return { ...token, error: "RefreshAccessTokenError" };
  }
  try {
    const issuer = process.env.KEYCLOAK_ISSUER;
    if (!issuer) throw new Error("KEYCLOAK_ISSUER is not configured.");
    const response = await refresh(`${issuer.replace(/\/$/, "")}/protocol/openid-connect/token`, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        client_id: process.env.KEYCLOAK_CLIENT_ID ?? "teck-dashboard",
        client_secret: clientSecret,
        grant_type: "refresh_token",
        refresh_token: token.refreshToken,
      }),
    });
    if (!response.ok) throw new Error(`Keycloak refresh failed with status ${response.status}.`);
    const refreshed = (await response.json()) as {
      access_token: string;
      expires_in: number;
      refresh_token?: string;
      id_token?: string;
    };
    return {
      ...token,
      accessToken: refreshed.access_token,
      accessTokenExpiresAt: Math.floor(Date.now() / 1000) + refreshed.expires_in,
      refreshToken: refreshed.refresh_token ?? token.refreshToken,
      idToken: refreshed.id_token ?? token.idToken,
      error: undefined,
    };
  } catch {
    return { ...token, error: "RefreshAccessTokenError" };
  }
}

export const authOptions: NextAuthOptions = {
  providers: [
    KeycloakProvider({
      clientId: process.env.KEYCLOAK_CLIENT_ID ?? "teck-dashboard",
      clientSecret,
      issuer: process.env.KEYCLOAK_ISSUER,
    }),
  ],
  secret: process.env.NEXTAUTH_SECRET,
  callbacks: {
    async jwt({ token, account }) {
      if (account) {
        token.accessToken = account.access_token;
        token.accessTokenExpiresAt = account.expires_at;
        token.refreshToken = account.refresh_token;
        token.idToken = account.id_token;
        token.error = undefined;
      }
      return refreshAccessToken(token);
    },
    async session({ session, token }) {
      if (token.error) session.error = token.error;
      return session;
    },
  },
};
