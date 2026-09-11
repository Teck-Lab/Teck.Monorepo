import { describe, expect, test } from "bun:test";

import {
  fetchThroughOmniRoute,
  searchThroughOmniRoute,
} from "../.omp/extensions/omniroute-research";

const environment = {
  OMNIROUTE_BASE_URL: "https://omniroute.example/v1/",
  OMNIROUTE_API_KEY: "proxy-managed",
};

describe("OmniRoute research routing", () => {
  test("search falls back from SearXNG to Firecrawl", async () => {
    const requests: Array<{ url: string; body: Record<string, unknown> }> = [];
    const fetch = async (input: string | URL | Request, init?: RequestInit) => {
      const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
      requests.push({ url: String(input), body });
      if (body.provider === "searxng-search") {
        return new Response("rate limited", { status: 429 });
      }
      return Response.json({
        id: "search-1",
        provider: "firecrawl",
        results: [{ title: "Example", url: "https://example.com", snippet: "Example result" }],
      });
    };

    const result = await searchThroughOmniRoute(
      { query: "example", limit: 1, recency: "week" },
      { environment, fetch: fetch as typeof globalThis.fetch },
    );

    expect(result.provider).toBe("firecrawl");
    expect(result.text).toContain("https://example.com");
    expect(requests).toEqual([
      {
        url: "https://omniroute.example/v1/search",
        body: { query: "example", provider: "searxng-search", max_results: 1, time_range: "week" },
      },
      {
        url: "https://omniroute.example/v1/search",
        body: { query: "example", provider: "firecrawl", max_results: 1, time_range: "week" },
      },
    ]);
  });

  test("URL fetch selects OmniRoute Firecrawl", async () => {
    let request:
      | { url: string; authorization: string | null; body: Record<string, unknown> }
      | undefined;
    const fetch = async (input: string | URL | Request, init?: RequestInit) => {
      const headers = new Headers(init?.headers);
      request = {
        url: String(input),
        authorization: headers.get("Authorization"),
        body: JSON.parse(String(init?.body)) as Record<string, unknown>,
      };
      return Response.json({
        provider: "firecrawl",
        url: "https://example.com",
        content: "# Example",
      });
    };

    const result = await fetchThroughOmniRoute("https://example.com", {
      environment,
      fetch: fetch as typeof globalThis.fetch,
    });

    expect(result.content).toBe("# Example");
    expect(request).toEqual({
      url: "https://omniroute.example/v1/web/fetch",
      authorization: "Bearer proxy-managed",
      body: {
        url: "https://example.com",
        provider: "firecrawl",
        format: "markdown",
        include_metadata: true,
      },
    });
  });
});
