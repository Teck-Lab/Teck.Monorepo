"use server";

import { revalidatePath } from "next/cache";
import { z } from "zod";
import { auth } from "../auth";
import { canManagePrices, isServiceGuid, parsePriceAmount } from "./authorization";
import { putPrice } from "./data";

const priceInput = z.object({
  listId: z.string().refine(isServiceGuid),
  productId: z.string().refine(isServiceGuid),
});

export async function savePrice(formData: FormData) {
  const session = await auth();
  if (!session?.user) throw new Error("Authentication required");
  if (!canManagePrices(session.user.roles)) throw new Error("Price management permission required");
  const parsed = priceInput.safeParse({
    listId: formData.get("listId"),
    productId: formData.get("productId"),
  });
  const amount = parsePriceAmount(String(formData.get("amount") ?? ""));
  if (!parsed.success || amount === undefined)
    throw new Error("Enter a valid non-negative price amount");
  await putPrice(parsed.data.listId, parsed.data.productId, amount);
  revalidatePath("/");
}
