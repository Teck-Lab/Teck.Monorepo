import type { DefaultSession } from "next-auth";

declare module "next-auth" {
  interface User {
    roles: string[];
  }

  interface Session {
    accessToken?: string;
    error?: string;
    user: DefaultSession["user"] & { roles: string[] };
  }
}

declare module "next-auth/jwt" {
  interface JWT {
    accessToken?: string;
    refreshToken?: string;
    accessTokenExpiresAt?: number;
    roles?: string[];
    error?: string;
  }
}
