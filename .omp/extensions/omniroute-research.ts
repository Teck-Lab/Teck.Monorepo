import type { ExtensionAPI, ExtensionContext } from "@oh-my-pi/pi-coding-agent";

const SEARCH_PROVIDERS = ["searxng-search", "firecrawl"] as const;
const FETCH_PROVIDER = "firecrawl";

interface SearchParams {
  query: string;
  recency?: "day" | "week" | "month" | "year";
  limit?: number;
  max_tokens?: number;
  temperature?: number;
  num_search_results?: number;
}

interface SearchResult {
  title?: string;
  url?: string;
  snippet?: string;
  published_at?: string | null;
}

interface SearchResponse {
  id?: string;
  provider?: string;
  results?: SearchResult[];
}

interface FetchResponse {
  provider?: string;
  url?: string;
  content?: string;
  metadata?: {
    title?: string | null;
    description?: string | null;
  } | null;
}

type FetchLike = typeof fetch;
type Environment = Record<string, string | undefined>;

function researchConfig(environment: Environment): { baseUrl: string; apiKey: string } {
  const baseUrl = environment.OMNIROUTE_BASE_URL?.trim().replace(/\/+$/, "");
  const apiKey = environment.OMNIROUTE_API_KEY?.trim();
  if (!baseUrl || !apiKey) {
    throw new Error("OmniRoute research requires OMNIROUTE_BASE_URL and OMNIROUTE_API_KEY");
  }
  return { baseUrl, apiKey };
}

async function omniRouteRequest<T>(
  path: string,
  body: Record<string, unknown>,
  fetchImpl: FetchLike,
  environment: Environment,
  signal?: AbortSignal,
): Promise<T> {
  const { baseUrl, apiKey } = researchConfig(environment);
  const response = await fetchImpl(`${baseUrl}${path}`, {
    method: "POST",
    headers: {
      Accept: "application/json",
      Authorization: `Bearer ${apiKey}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
    signal,
  });
  if (!response.ok) {
    const detail = (await response.text()).trim();
    throw new Error(`OmniRoute ${path} failed (${response.status})${detail ? `: ${detail}` : ""}`);
  }
  return (await response.json()) as T;
}

function searchLimit(params: SearchParams): number {
  const requested = params.num_search_results ?? params.limit ?? 10;
  return Math.max(1, Math.min(100, Math.trunc(requested)));
}
function formatSearchResults(response: SearchResponse): string {
  return (response.results ?? [])
    .filter((result): result is SearchResult & { url: string } => Boolean(result.url))
    .map((result, index) => {
      const title = result.title?.trim() || result.url;
      const date = result.published_at ? ` (${result.published_at})` : "";
      const snippet = result.snippet?.trim();
      return `[${index + 1}] ${title}${date}\n    ${result.url}${snippet ? `\n    ${snippet}` : ""}`;
    })
    .join("\n");
}

export async function searchThroughOmniRoute(
  params: SearchParams,
  options: {
    fetch?: FetchLike;
    environment?: Environment;
    signal?: AbortSignal;
  } = {},
): Promise<{ text: string; provider: string; requestId?: string }> {
  const fetchImpl = options.fetch ?? fetch;
  const environment = options.environment ?? process.env;
  const failures: string[] = [];

  for (const provider of SEARCH_PROVIDERS) {
    try {
      const response = await omniRouteRequest<SearchResponse>(
        "/search",
        {
          query: params.query,
          provider,
          max_results: searchLimit(params),
          ...(params.recency ? { time_range: params.recency } : {}),
        },
        fetchImpl,
        environment,
        options.signal,
      );
      const text = formatSearchResults(response);
      if (text) {
        return {
          text,
          provider: response.provider ?? provider,
          requestId: response.id,
        };
      }
      failures.push(`${provider}: no results`);
    } catch (error) {
      failures.push(`${provider}: ${error instanceof Error ? error.message : String(error)}`);
    }
  }

  throw new Error(`OmniRoute search providers failed: ${failures.join("; ")}`);
}

export async function fetchThroughOmniRoute(
  url: string,
  options: {
    fetch?: FetchLike;
    environment?: Environment;
    signal?: AbortSignal;
  } = {},
): Promise<FetchResponse> {
  return await omniRouteRequest<FetchResponse>(
    "/web/fetch",
    {
      url,
      provider: FETCH_PROVIDER,
      format: "markdown",
      include_metadata: true,
    },
    options.fetch ?? fetch,
    options.environment ?? process.env,
    options.signal,
  );
}

function isBareWebUrl(path: string): boolean {
  if (!/^https?:\/\//i.test(path)) return false;
  return !/:(?:raw|\d+(?:-\d+)?|-\d+)$/i.test(path);
}

async function invokeNativeRead(
  context: ExtensionContext,
  path: string,
  signal?: AbortSignal,
): Promise<{
  content: Array<{ type: "text"; text: string }>;
  details?: unknown;
  isError?: boolean;
}> {
  if (!context.invokeTool) {
    return {
      content: [{ type: "text", text: "Native read delegation is unavailable" }],
      isError: true,
    };
  }
  return await context.invokeTool({ path }, { signal });
}

export default function omniRouteResearchExtension(pi: ExtensionAPI): void {
  const z = pi.zod;

  pi.registerTool({
    name: "web_search",
    label: "Web Search",
    description:
      "Search current web content through OmniRoute. Uses configured SearXNG first and Firecrawl as fallback.",
    approval: "read",
    strict: true,
    parameters: z.object({
      query: z.string(),
      recency: z.enum(["day", "week", "month", "year"]).optional(),
      limit: z.number().optional(),
      max_tokens: z.number().optional(),
      temperature: z.number().optional(),
      num_search_results: z.number().optional(),
    }),
    async execute(_toolCallId, params, signal) {
      try {
        const result = await searchThroughOmniRoute(params, { signal });
        return {
          content: [{ type: "text", text: result.text }],
          details: { provider: result.provider, requestId: result.requestId },
        };
      } catch (error) {
        return {
          content: [{ type: "text", text: error instanceof Error ? error.message : String(error) }],
          isError: true,
        };
      }
    },
  });

  pi.registerTool({
    name: "read",
    label: "Read",
    description:
      "Read local files and internal resources with native OMP behavior. Bare HTTP(S) URLs are fetched as Markdown through OmniRoute Firecrawl.",
    approval: "read",
    strict: true,
    parameters: z.object({
      path: z.string(),
    }),
    async execute(_toolCallId, params, signal, _onUpdate, context) {
      if (!isBareWebUrl(params.path)) return await invokeNativeRead(context, params.path, signal);
      try {
        const result = await fetchThroughOmniRoute(params.path, { signal });
        if (!result.content?.trim()) throw new Error("OmniRoute Firecrawl returned no content");
        const notes = [
          `URL: ${result.url ?? params.path}`,
          `Reader: OmniRoute/${result.provider ?? FETCH_PROVIDER}`,
        ];
        if (result.metadata?.title) notes.push(`Title: ${result.metadata.title}`);
        return {
          content: [{ type: "text", text: `${notes.join("\n")}\n\n${result.content}` }],
          details: { kind: "url", provider: result.provider ?? FETCH_PROVIDER },
        };
      } catch (error) {
        return {
          content: [{ type: "text", text: error instanceof Error ? error.message : String(error) }],
          isError: true,
        };
      }
    },
  });
}
