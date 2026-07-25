# Pricing Service — Public Gateway Completion Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the existing Pricing service through the authenticated public gateway and mark its completed work package accordingly.

**Architecture:** Add a `pricing` YARP cluster with authenticated read/write route pairs for the service’s three existing public endpoint roots. A gateway integration theory proves every route group reaches the existing edge pipeline and inherits tenant, database-strategy, and exchanged-token forwarding. No Pricing service code changes.

**Tech Stack:** .NET 10, ASP.NET Core, YARP, xUnit v3, `WebApplicationFactory`, TestServer, JSON configuration.

## Global Constraints

- Only these implementation files may change: `src/services/gateway/public/appsettings.json`, `tests/integration/Gateway.Public.IntegrationTests/GatewayFlowTests.cs`, and `docs/superpowers/plans/services/pricing.md`.
- Do not modify Pricing service source, migrations, domain events, SharedKernel, Aspire host, solution files, `nx.json`, or the admin gateway.
- Do not change `src/services/gateway/public/appsettings.Development.json`; it overrides logging only and has no routing configuration.
- Every Pricing route uses `AuthorizationPolicy: authenticated` and `Metadata: { EdgeAccess: Authenticated }`, matching the existing order routes.
- The Pricing cluster destination is `http://pricing` with `AccessTokenClientName: pricing` so the edge pipeline passes its fail-closed audience validation and service discovery resolves the host.
- Do not create a git tag or push a branch.

## File Map

| File | Responsibility |
| --- | --- |
| `src/services/gateway/public/appsettings.json` | Route `/prices`, `/price-lists`, and `/exchange-rates` requests to the Pricing host through one authenticated YARP cluster. |
| `tests/integration/Gateway.Public.IntegrationTests/GatewayFlowTests.cs` | Prove all Pricing route groups forward trusted edge headers through the gateway. |
| `docs/superpowers/plans/services/pricing.md` | Replace its stale “new / fresh-session scope brief” metadata with completion status and implementation references. |

### Task 1: Add Pricing gateway routes and a regression theory

**Files:**
- Modify: `tests/integration/Gateway.Public.IntegrationTests/GatewayFlowTests.cs:5-255`
- Modify: `src/services/gateway/public/appsettings.json:11-31`

**Interfaces:**
- Consumes: the existing `GatewayFlowTests.GatewayFixture`, whose in-memory upstream echoes `X-TenantId`, `X-Tenant-DbStrategy`, and `Authorization`.
- Produces: a `pricing` YARP cluster and six routes: `pricing-prices-read`, `pricing-prices-write`, `pricing-price-lists-read`, `pricing-price-lists-write`, `pricing-exchange-rates-read`, and `pricing-exchange-rates-write`.

- [ ] **Step 1: Restore JasperFx host startup in the existing gateway fixture**

  Add this using after `using System.Net.Http.Json;`:

  ```csharp
  using JasperFx.CommandLine;
  ```

  Add this static constructor immediately inside `GatewayWebApplicationFactory`, before `ConfigureWebHost`:

  ```csharp
          static GatewayWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;
  ```

  `Program.cs` ends in `RunTeckServiceAsync`, which dispatches through JasperFx. Without this override, `WebApplicationFactory` returns from the entry point without starting its in-memory server. This is the same fixture pattern used by Basket, Catalog, and Pricing integration tests.

- [ ] **Step 2: Verify the existing gateway baseline test now passes**

  Run:

  ```bash
  dotnet test tests/integration/Gateway.Public.IntegrationTests/Gateway.Public.IntegrationTests.csproj --filter "FullyQualifiedName~GatewayFlowTests.AuthenticatedRequest_ForwardsTenantAndDbStrategyAndExchangedBearer"
  ```

  Expected: the existing authenticated `/orders/123` test passes with one passing test and zero failures.

- [ ] **Step 3: Write the failing route-regression theory**

  Insert this member and test after `AuthenticatedRequest_ForwardsTenantAndDbStrategyAndExchangedBearer` in `GatewayFlowTests`:

  ```csharp
      /// <summary>
      /// An authenticated request for each Pricing route group must be forwarded with the
      /// resolved <c>X-TenantId</c>, a non-empty <c>X-Tenant-DbStrategy</c>, and an exchanged
      /// <c>Authorization: Bearer ...</c> header.
      /// </summary>
      public static TheoryData<string, string> PricingRouteCases =>
          new()
          {
              { HttpMethod.Get.Method, "/prices/resolve?productId=00000000-0000-0000-0000-000000000001&currency=USD&quantity=1" },
              { HttpMethod.Get.Method, "/price-lists" },
              { HttpMethod.Post.Method, "/price-lists" },
              { HttpMethod.Get.Method, "/exchange-rates" },
              { HttpMethod.Put.Method, "/exchange-rates" },
          };

      [Theory]
      [MemberData(nameof(PricingRouteCases))]
      public async Task AuthenticatedPricingRequest_ForwardsTenantAndDbStrategyAndExchangedBearer(
          string method,
          string path)
      {
          using HttpClient client = fixture.CreateMockUserClient("tenant-a");
          using var request = new HttpRequestMessage(new HttpMethod(method), path);

          HttpResponseMessage response = await client.SendAsync(request);

          Assert.Equal(HttpStatusCode.OK, response.StatusCode);
          EchoedHeaders? echoed = await response.Content.ReadFromJsonAsync<EchoedHeaders>();
          Assert.NotNull(echoed);
          Assert.Equal("tenant-a", echoed!.TenantId);
          Assert.False(string.IsNullOrEmpty(echoed.TenantDbStrategy));
          Assert.StartsWith("Bearer ", echoed.Authorization, StringComparison.OrdinalIgnoreCase);
      }
  ```

- [ ] **Step 4: Run the new test and verify it fails before configuration exists**

  Run:

  ```bash
  dotnet test tests/integration/Gateway.Public.IntegrationTests/Gateway.Public.IntegrationTests.csproj --filter "FullyQualifiedName~GatewayFlowTests.AuthenticatedPricingRequest_ForwardsTenantAndDbStrategyAndExchangedBearer"
  ```

  Expected: five Pricing cases fail with `Expected: OK` / `Actual: NotFound`, because no `/prices`, `/price-lists`, or `/exchange-rates` YARP route is configured.

- [ ] **Step 5: Add the Pricing cluster and six authenticated routes**

  In `ReverseProxy:Routes`, append these entries after `order-write` (add a comma after the existing `order-write` object):

  ```json
      "pricing-prices-read": {
        "ClusterId": "pricing",
        "Match": { "Path": "/prices/{**catch-all}", "Methods": [ "GET" ] },
        "AuthorizationPolicy": "authenticated",
        "Metadata": { "EdgeAccess": "Authenticated" }
      },
      "pricing-prices-write": {
        "ClusterId": "pricing",
        "Match": { "Path": "/prices/{**catch-all}", "Methods": [ "POST", "PUT", "DELETE" ] },
        "AuthorizationPolicy": "authenticated",
        "Metadata": { "EdgeAccess": "Authenticated" }
      },
      "pricing-price-lists-read": {
        "ClusterId": "pricing",
        "Match": { "Path": "/price-lists/{**catch-all}", "Methods": [ "GET" ] },
        "AuthorizationPolicy": "authenticated",
        "Metadata": { "EdgeAccess": "Authenticated" }
      },
      "pricing-price-lists-write": {
        "ClusterId": "pricing",
        "Match": { "Path": "/price-lists/{**catch-all}", "Methods": [ "POST", "PUT", "DELETE" ] },
        "AuthorizationPolicy": "authenticated",
        "Metadata": { "EdgeAccess": "Authenticated" }
      },
      "pricing-exchange-rates-read": {
        "ClusterId": "pricing",
        "Match": { "Path": "/exchange-rates/{**catch-all}", "Methods": [ "GET" ] },
        "AuthorizationPolicy": "authenticated",
        "Metadata": { "EdgeAccess": "Authenticated" }
      },
      "pricing-exchange-rates-write": {
        "ClusterId": "pricing",
        "Match": { "Path": "/exchange-rates/{**catch-all}", "Methods": [ "POST", "PUT", "DELETE" ] },
        "AuthorizationPolicy": "authenticated",
        "Metadata": { "EdgeAccess": "Authenticated" }
      }
  ```

  In `ReverseProxy:Clusters`, add this sibling to `order` (add a comma after the `order` object):

  ```json
      "pricing": {
        "Destinations": {
          "primary": { "Address": "http://pricing", "AccessTokenClientName": "pricing" }
        }
      }
  ```

- [ ] **Step 6: Run the focused regression theory and verify it passes**

  Run:

  ```bash
  dotnet test tests/integration/Gateway.Public.IntegrationTests/Gateway.Public.IntegrationTests.csproj --filter "FullyQualifiedName~GatewayFlowTests.AuthenticatedPricingRequest_ForwardsTenantAndDbStrategyAndExchangedBearer"
  ```

  Expected: five Pricing cases pass; each receives `200 OK` from the echo upstream and validates the three forwarded headers.

- [ ] **Step 7: Run the full gateway integration test project**

  Run:

  ```bash
  dotnet test tests/integration/Gateway.Public.IntegrationTests/Gateway.Public.IntegrationTests.csproj
  ```

  Expected: all gateway integration tests pass (the original three tests plus five Pricing theory cases).

- [ ] **Step 8: Run the required security scan**

  Run:

  ```bash
  ./tools/security-scan.sh
  ```

  Expected: Semgrep, Gitleaks, and Trivy complete successfully; triage any finding against the changed configuration and test code before proceeding.

- [ ] **Step 9: Commit the route and regression-test change**

  Run:

  ```bash
  git add src/services/gateway/public/appsettings.json tests/integration/Gateway.Public.IntegrationTests/GatewayFlowTests.cs
  git commit -m "feat(gateway): route pricing requests"
  ```

  Expected: one conventional commit containing only the gateway configuration and integration test.

### Task 2: Mark the Pricing work package complete

**Files:**
- Modify: `docs/superpowers/plans/services/pricing.md:1-6`

**Interfaces:**
- Consumes: the completed gateway route delivery from Task 1 and the approved design documents at `docs/superpowers/specs/2026-07-05-pricing-service-design.md` and `docs/superpowers/specs/2026-07-23-pricing-audit-design.md`.
- Produces: a work-package header that accurately states Pricing is complete and points maintainers to its implementation and design.

- [ ] **Step 1: Replace the stale work-package introduction**

  Replace lines 1–6 with:

  ```markdown
  # Work Package: `pricing` service

  **Group:** commerce · **Tier:** 0 · **Status:** ✅ complete · **Branch:** `worktree-pricing-service`
  **Parallelism:** fully independent — consumes no events.

  This plan is complete. The approved design is in `docs/superpowers/specs/2026-07-05-pricing-service-design.md` and the implementation is in `src/services/commerce/pricing/`. Public-gateway routing for the Pricing service was added during completion; see `docs/superpowers/specs/2026-07-23-pricing-audit-design.md`.
  ```

  Leave the bounded context, domain, events, API surface, dependencies, shared-file touchpoints, and watch-items below the introduction unchanged.

- [ ] **Step 2: Verify the documentation change is clean and complete**

  Run:

  ```bash
  git diff --check -- docs/superpowers/plans/services/pricing.md
  git diff -- docs/superpowers/plans/services/pricing.md
  ```

  Expected: no whitespace errors; the diff changes the status to `✅ complete`, removes the fresh-session/scope-brief wording, and adds only the two implementation/design references.

- [ ] **Step 3: Commit the work-package status update**

  Run:

  ```bash
  git add docs/superpowers/plans/services/pricing.md
  git commit -m "docs(pricing): mark service complete"
  ```

  Expected: one conventional documentation commit containing only `pricing.md`.

## Verification Checklist

- [ ] The `pricing` cluster has `Address: http://pricing` and `AccessTokenClientName: pricing`.
- [ ] Six Pricing routes cover GET and POST/PUT/DELETE for `/prices`, `/price-lists`, and `/exchange-rates` with authenticated edge metadata.
- [ ] The Pricing gateway theory passes all five route cases and proves tenant, database-strategy, and exchanged-token forwarding.
- [ ] The full `Gateway.Public.IntegrationTests` project passes.
- [ ] `./tools/security-scan.sh` has been run and its findings, if any, are triaged.
- [ ] `docs/superpowers/plans/services/pricing.md` reports `✅ complete` and no longer calls the work package new or a fresh-session scope brief.
- [ ] No Pricing service source, migration, event, SharedKernel, Aspire, solution, admin-gateway, or Development configuration file changed.
