import type { components } from "@teck/api-client";
import { createTeckApiClient } from "@teck/api-client";
import { requireAccessToken } from "./session-token";

const PRODUCTS_PATH = "/api/v1/catalog/products" as const;
const PRICELISTS_PATH = "/api/v1/admin/pricing/price-lists" as const;

async function createClient() {
  const gatewayUrl = process.env.TECK_GATEWAY_URL;
  if (!gatewayUrl) throw new Error("TECK_GATEWAY_URL is required");
  return createTeckApiClient(gatewayUrl, await requireAccessToken());
}

export async function getProducts(): Promise<
  components["schemas"]["CatalogApplicationProductsResponsesProductSummaryDto"][]
> {
  const client = await createClient();
  const { data, error, response } = await client.GET(PRODUCTS_PATH, { params: { query: {} } });
  if (error || !response.ok || !data) throw new Error("Could not load products from the gateway");
  return data;
}

export async function getPriceLists(): Promise<
  components["schemas"]["PricingApplicationPricingResponsesPriceListDto"][]
> {
  const client = await createClient();
  const { data, error, response } = await client.GET(PRICELISTS_PATH);
  if (error || !response.ok || !data)
    throw new Error("Could not load price lists from the gateway");
  return data;
}

export async function putPrice(
  listId: string,
  productId: string,
  amount: number,
): Promise<components["schemas"]["PricingApplicationPricingResponsesPriceListDto"]> {
  const client = await createClient();
  const { data, error, response } = await client.PUT(
    "/api/v1/admin/pricing/price-lists/{id}/prices/{productId}",
    {
      params: { path: { id: listId, productId } },
      body: { amount, tiers: [] },
    },
  );
  if (error || !response.ok || !data) throw new Error("Could not save the price");
  return data;
}
