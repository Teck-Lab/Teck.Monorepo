# src/ — Source Code Conventions

All application and library source code lives under `src/`.

## Directory Rules

| Directory | Contains | Language | Framework |
|-----------|----------|----------|-----------|
| `services/` | .NET microservices | C# (.NET 10) | FastEndpoints, WolverineFx (mediator + message bus), EF Core |
| `shared/` | SharedKernel building blocks | C# | Cross-cutting: caching, CQRS, events, auth |
| `apps/` | Next.js applications | TypeScript | Next.js 16 App Router, Bun |
| `packages/` | Shared TypeScript libraries | TypeScript | Pure libraries (ui, api-client, config) |

## Hard Rules

- **No cross-group references** — `services/commerce/` projects cannot reference `services/operations/` projects
- **All shared code through SharedKernel** — services only depend on `shared/` projects, never on each other
- **Frontend and backend are decoupled** — `apps/` and `packages/` never reference .NET projects directly. API types are generated from OpenAPI specs in `specs/`.
- **One service per subdirectory** — each service is a self-contained clean architecture unit

## Service Discovery

See `src/services/AGENTS.md` for .NET microservice conventions.
See `src/apps/AGENTS.md` for TypeScript application conventions.
See `src/packages/AGENTS.md` for TypeScript library conventions.
See `src/shared/AGENTS.md` for SharedKernel rules.
