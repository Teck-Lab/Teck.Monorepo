import type { NextApiRequest, NextApiResponse } from "next";
import type { Session } from "next-auth";
import type { JWT } from "next-auth/jwt";
import { getServerSession } from "next-auth/next";
import { authOptions } from "./auth-options";

export type RouteSession = Session & { accessToken?: string };

export function getRouteSession(req: NextApiRequest, res: NextApiResponse) {
  return getServerSession(req, res, {
    ...authOptions,
    callbacks: {
      ...authOptions.callbacks,
      async session({ session, token }): Promise<RouteSession> {
        return {
          ...session,
          accessToken:
            typeof (token as JWT).accessToken === "string" ? (token as JWT).accessToken : undefined,
        };
      },
    },
  });
}
