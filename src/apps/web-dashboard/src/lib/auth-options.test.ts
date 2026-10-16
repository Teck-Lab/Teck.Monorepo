import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { decode, encode } from "next-auth/jwt";
import { authOptions, refreshAccessToken } from "./auth-options";

describe("route-local NextAuth session projection", () => {
  it("does not expose access tokens through the global session callback", async () => {
    const session = await authOptions.callbacks?.session?.({
      session: { expires: new Date(Date.now() + 60_000).toISOString() },
      token: { accessToken: "private" },
      user: { id: "staff" },
    } as never);
    assert.equal(session && "accessToken" in session, false);
  });
});

describe("Keycloak access-token refresh", () => {
  it("refreshes expired tokens and rotates refresh credentials", async () => {
    process.env.KEYCLOAK_ISSUER = "https://identity.example/realms/staff";
    const result = await refreshAccessToken(
      { accessToken: "expired", accessTokenExpiresAt: 1, refreshToken: "refresh-old" },
      async (input, init) => {
        assert.equal(
          String(input),
          "https://identity.example/realms/staff/protocol/openid-connect/token",
        );
        assert.equal(new URLSearchParams(String(init?.body)).get("refresh_token"), "refresh-old");
        return Response.json({
          access_token: "fresh",
          expires_in: 300,
          refresh_token: "refresh-new",
        });
      },
    );
    assert.equal(result.accessToken, "fresh");
    assert.equal(result.refreshToken, "refresh-new");
    assert.equal(result.error, undefined);
  });

  it("persists rotated refresh tokens across successive encrypted session cookies", async () => {
    process.env.KEYCLOAK_ISSUER = "https://identity.example/realms/staff";
    process.env.NEXTAUTH_SECRET = "test-secret";
    const originalFetch = globalThis.fetch;
    const refreshTokens: string[] = [];
    globalThis.fetch = async (_input, init) => {
      const refreshToken = new URLSearchParams(String(init?.body)).get("refresh_token");
      refreshTokens.push(refreshToken ?? "");
      const index = refreshTokens.length;
      return Response.json({
        access_token: `access-${index}`,
        expires_in: 300,
        refresh_token: `refresh-${index}`,
      });
    };
    try {
      const jwtCallback = authOptions.callbacks?.jwt;
      assert.ok(jwtCallback);
      const initial = await jwtCallback({
        token: {},
        account: {
          access_token: "access-expired",
          expires_at: 1,
          refresh_token: "refresh-initial",
          id_token: "id-initial",
          provider: "keycloak",
          type: "oauth",
          providerAccountId: "staff",
        },
        user: { id: "staff" },
        profile: undefined,
        isNewUser: false,
      });
      const firstCookie = await encode({
        token: initial,
        secret: process.env.NEXTAUTH_SECRET,
        maxAge: 60,
      });
      const firstRead = await decode({ token: firstCookie, secret: process.env.NEXTAUTH_SECRET });
      assert.equal(firstRead?.refreshToken, "refresh-1");

      const second = await jwtCallback({
        token: { ...firstRead, accessTokenExpiresAt: 1 },
        user: { id: "staff" },
        account: null,
        isNewUser: false,
      });
      const secondCookie = await encode({
        token: second,
        secret: process.env.NEXTAUTH_SECRET,
        maxAge: 60,
      });
      const secondRead = await decode({ token: secondCookie, secret: process.env.NEXTAUTH_SECRET });
      assert.deepEqual(refreshTokens, ["refresh-initial", "refresh-1"]);
      assert.equal(secondRead?.accessToken, "access-2");
      assert.equal(secondRead?.refreshToken, "refresh-2");
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it("marks failed refresh and absent refresh credentials for reauthentication", async () => {
    process.env.KEYCLOAK_ISSUER = "https://identity.example/realms/staff";
    const failed = await refreshAccessToken(
      { accessToken: "expired", accessTokenExpiresAt: 1, refreshToken: "revoked" },
      async () => new Response(null, { status: 400 }),
    );
    assert.equal(failed.error, "RefreshAccessTokenError");
    const missing = await refreshAccessToken(
      { accessToken: "expired", accessTokenExpiresAt: 1 },
      async () => {
        throw new Error("must not call Keycloak");
      },
    );
    assert.equal(missing.error, "RefreshAccessTokenError");
  });
});
