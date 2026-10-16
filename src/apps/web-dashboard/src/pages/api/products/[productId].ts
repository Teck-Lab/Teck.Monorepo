import { createCatalogClient } from "@teck/api-client";
import type { NextApiRequest, NextApiResponse } from "next";
import { getRouteSession } from "@/lib/route-session";

export default async function handler(req: NextApiRequest, res: NextApiResponse) {
  if (req.method !== "GET") {
    res.setHeader("Allow", "GET");
    return res.status(405).json({ detail: "Method not allowed." });
  }
  const token = await getRouteSession(req, res);
  if (typeof token?.accessToken !== "string") {
    return res
      .status(401)
      .json({ detail: "Sign in with your staff account before reading catalog products." });
  }
  const productId = req.query.productId;
  if (typeof productId !== "string")
    return res.status(400).json({ detail: "A product ID is required." });
  const client = createCatalogClient({
    baseUrl: process.env.CATALOG_API_BASE_URL ?? "http://localhost:8080",
    headers: { Authorization: `Bearer ${token.accessToken}` },
  });
  const result = await client.GET("/products/{productId}", { params: { path: { productId } } });
  if ([401, 403, 404].includes(result.response.status)) {
    return res
      .status(result.response.status)
      .json({ detail: "Your account cannot read this product." });
  }
  if (result.error || !result.data) {
    return res
      .status(result.response.status >= 500 ? 502 : 400)
      .json({ detail: "The catalog could not read the product." });
  }
  return res.status(200).json(result.data);
}
