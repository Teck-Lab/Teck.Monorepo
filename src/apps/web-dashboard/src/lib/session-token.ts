import { headers } from "next/headers";
import { getToken } from "next-auth/jwt";

export async function requireAccessToken(): Promise<string> {
  const token = await getToken({
    req: { headers: await headers() },
    secret: process.env.AUTH_SECRET,
    secureCookie: process.env.NODE_ENV === "production",
  });
  if (!token || typeof token.accessToken !== "string") throw new Error("Authentication required");
  if (typeof token.error === "string") throw new Error("Authentication expired; sign in again");
  return token.accessToken;
}
