import type { components } from "@teck/api-client";
import { createTeckApiClient } from "@teck/api-client";
import { requireAccessToken } from "./session-token";

export async function getProducts(): Promise<
  components["schemas"]["CatalogApplicationProductsResponsesProductSummaryDto"][]
> {
  const gatewayUrl = process.env.TECK_GATEWAY_URL;
  if (!gatewayUrl) throw new Error("TECK_GATEWAY_URL is required");
  const client = createTeckApiClient(gatewayUrl, await requireAccessToken());
  const { data, error, response } = await client.GET("/products");
  if (error || !response.ok || !data) throw new Error("Could not load products from the gateway");
  return data;
}

export async function getPriceLists(): Promise<
  components["schemas"]["PricingApplicationPricingResponsesPriceListDto"][]
> {
  const gatewayUrl = process.env.TECK_GATEWAY_URL;
  if (!gatewayUrl) throw new Error("TECK_GATEWAY_URL is required");
  const client = createTeckApiClient(gatewayUrl, await requireAccessToken());
  const { data, error, response } = await client.GET("/price-lists");
  if (error || !response.ok || !data)
    throw new Error("Could not load price lists from the gateway");
  return data;
}

export async function putPrice(
  listId: string,
  productId: string,
  amount: number,
): Promise<components["schemas"]["PricingApplicationPricingResponsesPriceListDto"]> {
  const gatewayUrl = process.env.TECK_GATEWAY_URL;
  if (!gatewayUrl) throw new Error("TECK_GATEWAY_URL is required");
  const client = createTeckApiClient(gatewayUrl, await requireAccessToken());
  const { data, error, response } = await client.PUT("/price-lists/{id}/prices/{productId}", {
    params: { path: { id: listId, productId } },
    body: { amount, tiers: [] },
  });
  if (error || !response.ok || !data) throw new Error("Could not save the price");
  return data;
}
