import { describe, expect, it } from "bun:test";
import { getAuthEnvironment } from "../auth-config";
import { canManagePrices, isServiceGuid, parsePriceAmount } from "./authorization";

describe("staff price editing authorization", () => {
  it("allows managers but keeps readers and users without role read-only", () => {
    expect(canManagePrices(["platform-manager", "platform-reader"])).toBe(true);
    expect(canManagePrices(["platform-reader"])).toBe(false);
    expect(canManagePrices([])).toBe(false);
  });

  it("accepts .NET sequential GUID identifiers with non-RFC variant bits", () => {
    expect(isServiceGuid("6be90000-c2e2-4e14-f06d-08df3080facf")).toBe(true);
    expect(isServiceGuid("6be90000-c2e2-4e14-3923-08df3081016c")).toBe(true);
    expect(isServiceGuid("not-an-id")).toBe(false);
  });
});

describe("price amount validation", () => {
  it("accepts zero and finite non-negative amounts within the API range", () => {
    expect(parsePriceAmount("0")).toBe(0);
    expect(parsePriceAmount("125.50")).toBe(125.5);
    expect(parsePriceAmount("999999999999")).toBe(999999999999);
  });

  it("rejects empty, negative, non-finite, and out-of-range input", () => {
    expect(parsePriceAmount("")).toBeUndefined();
    expect(parsePriceAmount("-0.01")).toBeUndefined();
    expect(parsePriceAmount("NaN")).toBeUndefined();
    expect(parsePriceAmount("Infinity")).toBeUndefined();
    expect(parsePriceAmount("1000000000000")).toBeUndefined();
  });
});

describe("dashboard OIDC configuration", () => {
  it("requires explicitly configured confidential Keycloak credentials", () => {
    expect(() => getAuthEnvironment({})).toThrow("KEYCLOAK_ISSUER is required");
    expect(() => getAuthEnvironment({ KEYCLOAK_ISSUER: "https://sso.test/realms/teck" })).toThrow(
      "KEYCLOAK_CLIENT_ID is required",
    );
    expect(
      getAuthEnvironment({
        KEYCLOAK_ISSUER: "https://sso.test/realms/teck",
        KEYCLOAK_CLIENT_ID: "test-client",
        KEYCLOAK_CLIENT_SECRET: "server-only-secret",
        AUTH_SECRET: "a-test-secret-of-at-least-32-characters",
      }).keycloakClientSecret,
    ).toBe("server-only-secret");
  });
});
