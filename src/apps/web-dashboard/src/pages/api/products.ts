import { createCatalogClient, type paths } from "@teck/api-client";
import type { NextApiRequest, NextApiResponse } from "next";
import { z } from "zod";
import { getRouteSession } from "@/lib/route-session";

type CreateProduct = paths["/products"]["post"]["requestBody"]["content"]["application/json"];
const createProductSchema = z
  .object({
    name: z.string().min(1),
    description: z.string().nullable().optional(),
    categoryId: z.string().uuid().nullable().optional(),
    sku: z.string().min(1),
    sellPriceAmount: z.number().finite().nonnegative().optional(),
    sellPriceCurrency: z.string().length(3),
  })
  .strict() satisfies z.ZodType<CreateProduct>;

export function validateCreateProductBody(body: unknown) {
  return createProductSchema.safeParse(body);
}

function sendJson(res: NextApiResponse, body: unknown, status: number) {
  res.status(status).json(body);
}

export default async function handler(req: NextApiRequest, res: NextApiResponse) {
  if (req.method !== "POST") {
    res.setHeader("Allow", "POST");
    return sendJson(res, { detail: "Method not allowed." }, 405);
  }
  const trustedOrigin = process.env.DASHBOARD_ORIGIN;
  if (!trustedOrigin) return sendJson(res, { detail: "Dashboard origin is not configured." }, 500);
  if (req.headers.origin !== trustedOrigin)
    return sendJson(res, { detail: "Cross-origin requests are not allowed." }, 403);
  const token = await getRouteSession(req, res);
  if (typeof token?.accessToken !== "string")
    return sendJson(
      res,
      { detail: "Sign in with your staff account before creating products." },
      401,
    );
  const parsed = validateCreateProductBody(req.body);
  if (!parsed.success)
    return sendJson(
      res,
      { detail: "Request body does not match the product contract.", issues: parsed.error.issues },
      400,
    );
  const client = createCatalogClient({
    baseUrl: process.env.CATALOG_API_BASE_URL ?? "http://localhost:8080",
    headers: { Authorization: `Bearer ${token.accessToken}` },
  });
  const result = await client.POST("/products", { body: parsed.data });
  if (result.response.status === 401 || result.response.status === 403)
    return sendJson(
      res,
      { detail: "Your Keycloak account is not authorized to create catalog products." },
      result.response.status,
    );
  if (result.response.status === 400) return sendJson(res, result.error, 400);
  if (result.error || !result.data)
    return sendJson(
      res,
      { detail: "Catalog product creation failed." },
      result.response.status >= 500 ? 502 : 400,
    );
  return sendJson(res, result.data, 201);
}
