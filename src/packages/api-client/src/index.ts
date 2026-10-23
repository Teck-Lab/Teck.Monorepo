import createClient from "openapi-fetch";
import type { paths } from "./generated/schema";

export type { components, operations, paths } from "./generated/schema";

export function createTeckApiClient(
  baseUrl: string,
  accessToken: string,
  fetchImpl?: typeof fetch,
) {
  return createClient<paths>({
    baseUrl,
    headers: { Authorization: `Bearer ${accessToken}` },
    fetch: fetchImpl,
  });
}
