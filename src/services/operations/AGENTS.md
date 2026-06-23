# src/services/operations/ — Operations Group

Supporting infrastructure services. Same 3-project structure as commerce. PostgreSQL only, EF Core Migrations in-app. Versioned together as `operations@{version}`.

## Services

| Service | Projects | Key Characteristics |
|---------|----------|-------------------|
| **billing** | Domain + Application + Host (3) | Invoicing, payment processing |
| **device** | Domain + Application + Host + VendorWorker (4) | Device management, has background worker |
| **location** | Domain + Application + Host (3) | Location data, geocoding |
| **statistic** | Domain + Application + Host (3) | Analytics, real-time statistics |

## Differences from Commerce

- **device has VendorWorker** — a background worker project for vendor data sync
- **statistic was single-provider** — now standardizes on PostgreSQL with EF Core Migrations like all services
