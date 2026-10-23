export function canManagePrices(roles: readonly string[]): boolean {
  return roles.includes("platform-manager");
}
// Guid.CreateVersion7/COMB-style identifiers may not carry RFC 4122 variant bits.
// Accept the wire format used by .NET services, while rejecting malformed IDs.
export function isServiceGuid(value: string): boolean {
  return /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(value);
}

export function parsePriceAmount(raw: string): number | undefined {
  if (raw.trim() === "") return undefined;
  const amount = Number(raw);
  if (!Number.isFinite(amount) || amount < 0 || amount > 999_999_999_999) return undefined;
  return amount;
}
