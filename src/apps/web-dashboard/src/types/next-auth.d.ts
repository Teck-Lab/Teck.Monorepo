import type { DefaultSession } from "next-auth";

declare module "next-auth" {
  interface Session {
    error?: string;
    user?: DefaultSession["user"];
  }
}

declare module "next-auth/jwt" {
  interface JWT {
    accessToken?: string;
    accessTokenExpiresAt?: number;
    refreshToken?: string;
    error?: string;
    idToken?: string;
  }
}
