# src/services/commerce/ — Commerce Group

Core e-commerce domain. All 5 services are full clean architecture skeletons (7 projects each). Versioned together as `commerce@{version}`.

## Services

| Service | Key Capabilities | Integration Events (Emits) | Integration Events (Consumes) |
|---------|-----------------|---------------------------|-------------------------------|
| **basket** | Cart management, line items, checkout | BasketCheckedOut | ProductPriceChanged, OrderPlaced |
| **catalog** | Products, categories, variants, pricing | ProductPriceChanged | — |
| **customer** | **Platform tenant authority.** Global tenant registry (`Tenant` entity), per-tenant database-strategy metadata, remote `GetTenantDatabaseInfo` gRPC handler (FastEndpoints `ICommandHandler`, not WolverineFx). No HTTP endpoints; no `ITenantScoped` aggregates (the Tenant entity IS the authority). Migration seed: `dev` tenant (id `00000000-0000-0000-0000-0000000000a1`, strategy `shared`). | CustomerCreated | — |
| **order** | Order lifecycle, line items, fulfillment | OrderPlaced, OrderShipped | BasketCheckedOut, CustomerCreated |
| **product** | Product catalog, inventory | — | — |

## Standard Service Structure (All Commerce Services)

Each service follows this exact layout. See `order/AGENTS.md` for a concrete example.

```
{service}/
├── {Service}.Domain/
│   ├── Entities/                    ← aggregate roots, entities
│   ├── ValueObjects/                ← immutable value types
│   ├── DomainEvents/                ← domain events
│   ├── Services/                    ← domain services (stateless business logic)
│   └── {Service}.Domain.csproj
│
├── {Service}.Application/
│   ├── {Capability}/                ← one per business capability
│   │   ├── Features/
│   │   │   └── {UseCase}/V1/
│   │   │       ├── {UseCase}Request.cs
│   │   │       ├── {UseCase}RequestValidator.cs
│   │   │       └── {UseCase}Handler.cs
│   │   ├── Responses/
│   │   ├── ReadModels/
│   │   └── Mapping/                  ← Mapperly mappers (entity ↔ DTO)
│   ├── EventHandlers/
│   │   └── DomainEvents/            ← domain event handlers
│   └── {Service}.Application.csproj
│
├── {Service}.Host/
│   ├── Endpoints/                   ← FastEndpoints endpoint classes ONLY
│   ├── Database/
│   │   ├── Migrations/              ← EF Core auto-generated migrations
│   │   ├── {Service}DbContext.cs
│   │   └── Configurations/          ← EF Core entity configurations
│   ├── Infrastructure/              ← external clients, WolverineFx config
│   ├── Program.cs                   ← --migrate flag switches between modes
│   └── {Service}.Host.csproj
│
└── Directory.Build.props
```

## Migration Strategy

**Same image, two modes.** The Host project includes migration logic directly. No separate migration project.

```csharp
// Program.cs
if (args.Contains("--migrate"))
{
    await RunMigrationsAsync(app);
    return 0;   // exit after migration
}
app.Run();       // normal startup
```

In Kubernetes, the migration runs as an init container before the application starts:

```yaml
initContainers:
  - name: migration
    image: ghcr.io/teck-lab/teck-cloud/{service}-api:{version}
    args: ["--migrate"]
    envFrom:
      - secretRef: { name: {service}-postgres }
containers:
  - name: {service}-api
    image: ghcr.io/teck-lab/teck-cloud/{service}-api:{version}
```

Benefits: no separate migration project, no separate Docker image, no coordination. Kubernetes guarantees init container completes before main container starts. Rollback is "revert the image tag" -- migrations must be backward-compatible.

## Dependency Rules (Enforced by Architecture Tests)

```
{Service}.Domain        ← references: SharedKernel.Core
{Service}.Application   ← references: {Service}.Domain, SharedKernel.Core, SharedKernel.Infrastructure
{Service}.Host          ← references: {Service}.Application, {Service}.Domain, SharedKernel.*, ServiceDefaults
```

Application NEVER references Host. Domain NEVER references anything. All inner-to-outer rule is preserved.

## Infrastructure Extension Pattern

Each service MUST implement these two methods in {Service}.Host:

```csharp
public static void AddHostServices(this WebApplicationBuilder builder, Assembly applicationAssembly)
public static void UseHostServices(this IApplicationBuilder app)
```

## Infrastructure Extension Pattern

Every service MUST implement these two methods:

```csharp
// In {Service}.Infrastructure:
public static void AddInfrastructureServices(this WebApplicationBuilder builder, Assembly applicationAssembly)
public static void UseInfrastructureServices(this IApplicationBuilder app)
```

## Tests

| Test Type | Project | Location |
|-----------|---------|----------|
| Unit | {Service}.UnitTests | tests/unit/ |
| Integration | {Service}.IntegrationTests | tests/integration/ |
| Architecture | {Service}.Architecture.UnitTests | tests/architecture/ |
