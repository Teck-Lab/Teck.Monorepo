import { describe, expect, it } from "bun:test";
import { createTeckApiClient } from "../../../src/packages/api-client/src/index";

describe("createTeckApiClient", () => {
  it("constructs calls against gateway base URL with server bearer token", async () => {
    let observedUrl = "";
    let observedAuthorization = "";
    const client = createTeckApiClient(
      "https://gateway.example",
      "server-token",
      async (input, init) => {
        const request = input instanceof Request ? input : new Request(input, init);
        observedUrl = request.url;
        observedAuthorization = request.headers.get("Authorization") ?? "";
        return new Response(JSON.stringify([]), {
          headers: { "Content-Type": "application/json" },
        });
      },
    );
    const result = await client.GET("/products");
    expect(result.response.status).toBe(200);
    expect(observedUrl).toBe("https://gateway.example/products");
    expect(observedAuthorization).toBe("Bearer server-token");
  });
});
