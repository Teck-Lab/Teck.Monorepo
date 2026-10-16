import assert from "node:assert/strict";
import { createCatalogClient } from "../src/index.ts";

const requests = [];
const client = createCatalogClient({
  baseUrl: "https://gateway.test/api/catalog",
  headers: { Authorization: "Bearer smoke-token" },
  fetch: async (input) => {
    const request = input instanceof Request ? input : new Request(input);
    const url = request.url;
    const body = await request.clone().text();
    requests.push({ input: url, method: request.method, headers: request.headers, body });

    if (
      url.endsWith("/products") &&
      request.method === "POST" &&
      body.includes('"sellPriceAmount":129.95')
    ) {
      return Response.json(
        {
          id: "018f4c00-0000-7000-8000-000000000001",
          name: "Desk",
          description: null,
          categoryId: null,
          isActive: true,
          variants: [],
        },
        { status: 201, headers: { Location: "/products/018f4c00-0000-7000-8000-000000000001" } },
      );
    }

    if (url.endsWith("/products") && request.method === "POST") {
      return Response.json(
        {
          type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
          title: "One or more validation errors occurred.",
          status: 400,
          instance: "/products",
          traceId: "smoke-trace-id",
          errors: { SellPriceAmount: ["must be greater than zero"] },
        },
        { status: 400, headers: { "Content-Type": "application/problem+json" } },
      );
    }

    if (
      url.endsWith("/products/018f4c00-0000-7000-8000-000000000001") &&
      request.method === "GET"
    ) {
      return Response.json({
        id: "018f4c00-0000-7000-8000-000000000001",
        name: "Desk",
        description: "Oak writing desk",
        categoryId: "018f4c00-0000-7000-8000-000000000002",
        isActive: true,
        variants: [],
      });
    }

    if (url.endsWith("/products/not-found") && request.method === "GET") {
      return Response.json(
        { statusCode: 404, message: "Product not found", error: "Not Found" },
        { status: 404, headers: { "Content-Type": "application/problem+json" } },
      );
    }

    throw new Error(`Unexpected request: ${request.method} ${url}`);
  },
});

const created = await client.POST("/products", {
  body: {
    name: "Desk",
    description: null,
    categoryId: null,
    sku: "DESK-001",
    sellPriceAmount: 129.95,
    sellPriceCurrency: "USD",
  },
});
assert.equal(created.response.status, 201);
assert.deepEqual(created.data, {
  id: "018f4c00-0000-7000-8000-000000000001",
  name: "Desk",
  description: null,
  categoryId: null,
  isActive: true,
  variants: [],
});
assert.equal(
  created.response.headers.get("Location"),
  "/products/018f4c00-0000-7000-8000-000000000001",
);
assert.equal(requests[0].method, "POST");
assert.equal(requests[0].headers.get("Content-Type"), "application/json");
assert.equal(requests[0].input, "https://gateway.test/api/catalog/products");
assert.equal(requests[0].headers.get("Authorization"), "Bearer smoke-token");
assert.deepEqual(JSON.parse(requests[0].body), {
  name: "Desk",
  description: null,
  categoryId: null,
  sku: "DESK-001",
  sellPriceAmount: 129.95,
  sellPriceCurrency: "USD",
});

const rejected = await client.POST("/products", {
  body: {
    name: "Desk",
    description: null,
    categoryId: null,
    sku: "DESK-001",
    sellPriceAmount: -1,
    sellPriceCurrency: "USD",
  },
});
assert.equal(rejected.response.status, 400);
assert.equal(rejected.response.headers.get("Content-Type"), "application/problem+json");
assert.equal(rejected.error?.title, "One or more validation errors occurred.");
assert.equal(rejected.error?.status, 400);
assert.equal(rejected.error?.instance, "/products");
assert.equal(rejected.error?.traceId, "smoke-trace-id");
assert.deepEqual(rejected.error?.errors?.SellPriceAmount, ["must be greater than zero"]);
assert.equal(requests[1].method, "POST");
assert.equal(requests[1].input, "https://gateway.test/api/catalog/products");

const found = await client.GET("/products/{productId}", {
  params: { path: { productId: "018f4c00-0000-7000-8000-000000000001" } },
});
assert.equal(found.response.status, 200);
assert.deepEqual(found.data, {
  id: "018f4c00-0000-7000-8000-000000000001",
  name: "Desk",
  description: "Oak writing desk",
  categoryId: "018f4c00-0000-7000-8000-000000000002",
  isActive: true,
  variants: [],
});
assert.equal(requests[2].method, "GET");
assert.equal(
  requests[2].input,
  "https://gateway.test/api/catalog/products/018f4c00-0000-7000-8000-000000000001",
);
assert.equal(requests[2].headers.get("Authorization"), "Bearer smoke-token");

const missing = await client.GET("/products/{productId}", {
  params: { path: { productId: "not-found" } },
});
assert.equal(missing.response.status, 404);
assert.equal(missing.error?.statusCode, 404);
assert.equal(missing.error?.message, "Product not found");
assert.equal(missing.error?.error, "Not Found");
assert.equal(requests[3].method, "GET");
assert.equal(requests[3].input, "https://gateway.test/api/catalog/products/not-found");
assert.equal(requests[3].headers.get("Authorization"), "Bearer smoke-token");
