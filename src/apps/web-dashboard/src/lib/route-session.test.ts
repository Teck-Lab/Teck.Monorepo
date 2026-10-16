import assert from "node:assert/strict";
import { once } from "node:events";
import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { describe, it } from "node:test";
import { encode } from "next-auth/jwt";
import products from "../pages/api/products";

function addNextResponseMethods(res: ServerResponse) {
  const response = res as ServerResponse & {
    status: (status: number) => ServerResponse;
    json: (body: unknown) => void;
  };
  response.status = (status) => {
    response.statusCode = status;
    return response;
  };
  response.json = (body) => {
    response.setHeader("Content-Type", "application/json");
    response.end(JSON.stringify(body));
  };
  return response;
}

function apiRequest(req: IncomingMessage, body: unknown) {
  const pair = (req.headers.cookie ?? "").split(";", 1)[0] ?? "";
  const separator = pair.indexOf("=");
  const cookies = separator > 0 ? { [pair.slice(0, separator)]: pair.slice(separator + 1) } : {};
  return Object.assign(req, { cookies, query: {}, body });
}

const cookieName = "next-auth.session-token";
const validProduct = {
  name: "Desk",
  sku: "DESK-1",
  sellPriceCurrency: "USD",
  sellPriceAmount: 100,
};

describe("Pages API product route", () => {
  it("persists two token rotations through successive handler requests", async () => {
    process.env.NEXTAUTH_SECRET = "test-secret";
    process.env.KEYCLOAK_ISSUER = "https://identity.example/realms/staff";
    process.env.DASHBOARD_ORIGIN = "http://localhost";
    const nativeFetch = globalThis.fetch;
    const refreshTokens: string[] = [];
    const authorizations: string[] = [];
    const catalog = createServer((req, res) => {
      authorizations.push(req.headers.authorization ?? "");
      res.statusCode = 201;
      res.setHeader("Content-Type", "application/json");
      res.end(JSON.stringify({ id: "product-1", ...validProduct }));
    });
    catalog.listen(0, "127.0.0.1");
    await once(catalog, "listening");
    const catalogAddress = catalog.address();
    assert.ok(catalogAddress && typeof catalogAddress !== "string");
    process.env.CATALOG_API_BASE_URL = `http://127.0.0.1:${catalogAddress.port}`;
    globalThis.fetch = async (input, init) => {
      if (String(input).includes("/protocol/openid-connect/token")) {
        const token = new URLSearchParams(String(init?.body)).get("refresh_token") ?? "";
        refreshTokens.push(token);
        return Response.json({
          access_token: `access-${refreshTokens.length}`,
          expires_in: 0,
          refresh_token: `refresh-${refreshTokens.length}`,
        });
      }
      return nativeFetch(input, init);
    };
    const seed = await encode({
      token: { accessToken: "expired", accessTokenExpiresAt: 1, refreshToken: "refresh-initial" },
      secret: process.env.NEXTAUTH_SECRET,
    });
    const server = createServer((incoming, outgoing) => {
      void products(
        apiRequest(incoming, validProduct) as never,
        addNextResponseMethods(outgoing) as never,
      );
    });
    server.listen(0, "127.0.0.1");
    await once(server, "listening");
    const address = server.address();
    assert.ok(address && typeof address !== "string");
    let cookie = `${cookieName}=${seed}`;
    try {
      for (let index = 1; index <= 2; index++) {
        const response: Response = await fetch(`http://127.0.0.1:${address.port}/api/products`, {
          method: "POST",
          headers: { cookie, origin: "http://localhost" },
          body: JSON.stringify(validProduct),
        });
        assert.equal(response.status, 201);
        assert.deepEqual(await response.json(), { id: "product-1", ...validProduct });
        const rotatedCookie = response.headers
          .getSetCookie()
          .find((value: string) => value.startsWith(`${cookieName}=`));
        assert.ok(rotatedCookie, "NextAuth persists its rotated session cookie");
        cookie = rotatedCookie.split(";", 1)[0] ?? "";
      }
      assert.deepEqual(refreshTokens, ["refresh-initial", "refresh-1"]);
      assert.deepEqual(authorizations, ["Bearer access-1", "Bearer access-2"]);
    } finally {
      globalThis.fetch = nativeFetch;
      server.close();
      catalog.close();
    }
  });

  it("rejects cross-origin POST, bad body, and unauthenticated requests", async () => {
    process.env.NEXTAUTH_SECRET = "test-secret";
    process.env.DASHBOARD_ORIGIN = "http://localhost";
    const server = createServer((incoming, outgoing) => {
      const body = incoming.headers["x-body"]
        ? JSON.parse(incoming.headers["x-body"] as string)
        : {};
      void products(apiRequest(incoming, body) as never, addNextResponseMethods(outgoing) as never);
    });
    server.listen(0, "127.0.0.1");
    await once(server, "listening");
    const address = server.address();
    assert.ok(address && typeof address !== "string");
    const url = `http://127.0.0.1:${address.port}/api/products`;
    const validCookie = await encode({
      token: {
        accessToken: "still-valid",
        accessTokenExpiresAt: Math.floor(Date.now() / 1000) + 300,
        refreshToken: "refresh-valid",
      },
      secret: process.env.NEXTAUTH_SECRET,
    });
    try {
      const crossOrigin = await fetch(url, {
        method: "POST",
        headers: { origin: "http://evil.example", "x-body": "{}" },
      });
      assert.equal(crossOrigin.status, 403);
      const badBody = await fetch(url, {
        method: "POST",
        headers: {
          origin: "http://localhost",
          cookie: `${cookieName}=${validCookie}`,
          "x-body": JSON.stringify({ ...validProduct, sku: 7 }),
        },
      });
      assert.equal(badBody.status, 400);
      const unauthenticated = await fetch(url, {
        method: "POST",
        headers: { origin: "http://localhost", "x-body": "{}" },
      });
      assert.equal(unauthenticated.status, 401);
    } finally {
      server.close();
    }
  });
});
