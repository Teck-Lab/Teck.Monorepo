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
    if (url.endsWith("/products") && request.method === "POST") {
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
    return Response.json(
      {
        type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        title: "One or more validation errors occurred.",
        status: 400,
        instance: "/products",
        traceId: "smoke-trace-id",
        errors: { SellPriceCurrency: ["must be an ISO currency code"] },
      },
      { status: 400, headers: { "Content-Type": "application/problem+json" } },
    );
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
assert.equal(created.data?.id, "018f4c00-0000-7000-8000-000000000001");
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

const rejected = await client.GET("/products/{productId}", {
  params: { path: { productId: "bad-id" } },
});
assert.equal(rejected.response.status, 400);
assert.equal(rejected.error?.title, "One or more validation errors occurred.");
assert.equal(rejected.error?.status, 400);
assert.equal(rejected.error?.instance, "/products");
assert.equal(rejected.error?.traceId, "smoke-trace-id");
assert.deepEqual(rejected.error?.errors?.SellPriceCurrency, ["must be an ISO currency code"]);
assert.match(requests[1].input, /\/products\/bad-id$/);
