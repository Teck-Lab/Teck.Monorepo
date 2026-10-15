import createClient from "openapi-fetch";
import type { paths } from "./generated/catalog";

export type { components, operations, paths } from "./generated/catalog";

export function createCatalogClient(options: {
  baseUrl: string;
  fetch?: typeof fetch;
  headers?: HeadersInit;
}) {
  return createClient<paths>({
    baseUrl: options.baseUrl,
    fetch: options.fetch,
    headers: options.headers,
  });
}
