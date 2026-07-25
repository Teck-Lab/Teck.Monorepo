# Customer PR Conflict Resolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve PR #14 against `main`, validate the customer host, and squash-merge the PR.

**Architecture:** Integrate `origin/main` into the PR branch. Retain the PR's identity and Keycloak registrations plus the existing code-generation guard and development seed, then configure handlers and cross-service delivery through the shared `AddTeckMessaging` wrapper. Preserve `main`'s billing resource in `AppHost.cs`.

**Tech Stack:** Git, GitHub CLI, .NET 10, FastEndpoints, WolverineFx, Keycloak authentication, EF Core.

## Global Constraints

- Leave `.devcontainer/litellm/config.yaml`, `.devcontainer/opencode/oh-my-opencode-slim.jsonc`, `.devcontainer/opencode/opencode-mem.jsonc`, `docs/self-review/user-profile-analysis-2026-07-21.md`, and this untracked plan file untouched and unstaged.
- Merge `origin/main`, not the local `main` design-document commit, into the PR branch.
- Preserve `billingDb` and `Projects.Billing_Host` in `src/aspire/Teck.AppHost/AppHost.cs`.
- Do not delete the PR source branch.
- Squash with the PR title: `feat(customer): complete customer service — profile aggregate, HTTP CRUD, CustomerCreated event`.

---

### Task 1: Resolve `main` into the PR branch

**Files:**
- Modify: `src/services/commerce/customer/Customer.Host/Program.cs:1-67`
- Preserve: `src/aspire/Teck.AppHost/AppHost.cs:1-105`

**Interfaces:**
- Consumes: `ICustomerIdentityAccessor`, `CustomerIdentityAccessor`, `AddKeycloak`, and `AddTeckMessaging`.
- Produces: a customer host with authentication, Wolverine handler discovery, development seeding, and the billing Aspire resource.

- [ ] **Step 1: Create the local PR branch.** Run `git fetch origin main feat/customer-completion`, then `git switch --create feat/customer-completion --track origin/feat/customer-completion`. Verify `git status --short` shows only the pre-existing unstaged `.devcontainer/litellm/config.yaml` and `.devcontainer/opencode/oh-my-opencode-slim.jsonc` modifications plus this untracked plan file.

- [ ] **Step 2: Reproduce the merge conflict.** Run `git merge origin/main` and `git status --short`. Expected: only `src/services/commerce/customer/Customer.Host/Program.cs` is unmerged; `AppHost.cs` keeps its billing resource.

- [ ] **Step 3: Resolve the host startup configuration.** Retain `AddScoped<ICustomerIdentityAccessor, CustomerIdentityAccessor>()`, `AddKeycloak(...)`, `CodeGenerationDetector.IsRunningGeneration()`, and `SeedDevTenantAsync(app)`. Configure the handler assembly and platform transport through `builder.AddTeckMessaging(typeof(CustomerDbContext).Assembly, "CustomerWrite");`; do not leave a direct `UseWolverine(...)` block in `Program.cs`. Run `git diff --cached --check` and `git diff -- src/aspire/Teck.AppHost/AppHost.cs`; record inherited whitespace findings separately from resolution-file validation and expect no AppHost diff.

- [ ] **Step 4: Commit only the resolution.** Run `git commit -m "chore(customer): resolve main merge conflict"`, followed by `git show --check --stat --oneline HEAD`. Expected: a conventional conflict-resolution commit without a billing-resource deletion.

### Task 2: Validate the resolved service

**Files:**
- Test: `tests/unit/Customer.UnitTests/Customer.UnitTests.csproj`
- Test: `tests/architecture/Customer.Architecture.UnitTests/Customer.Architecture.UnitTests.csproj`
- Test: `tests/integration/Customer.IntegrationTests/Customer.IntegrationTests.csproj`

**Interfaces:**
- Consumes: the resolved customer-host startup configuration.
- Produces: build, test, and security evidence for the PR merge.

- [ ] **Step 1: Build the host.** Run `dotnet build src/services/commerce/customer/Customer.Host/Customer.Host.csproj`. Expected: zero errors.

- [ ] **Step 2: Run focused tests.** Run `dotnet test tests/unit/Customer.UnitTests/Customer.UnitTests.csproj`, `dotnet test tests/architecture/Customer.Architecture.UnitTests/Customer.Architecture.UnitTests.csproj`, and `dotnet test tests/integration/Customer.IntegrationTests/Customer.IntegrationTests.csproj`. Expected: all projects pass; the integration project starts its PostgreSQL Testcontainer.

- [ ] **Step 3: Run the security gate.** Run `./tools/security-scan.sh`. Triage every Semgrep, Gitleaks, or Trivy finding against the resolved files before merging.

### Task 3: Push the resolved PR and verify server checks

**Files:**
- Remote update: `origin/feat/customer-completion`
- Merge target: GitHub PR #14 into `main` after final whole-branch review

**Interfaces:**
- Consumes: the validated conflict-resolution commit.
- Produces: a reviewed, mergeable PR whose source branch remains intact.

- [ ] **Step 1: Push and wait for checks.** Run `git push origin feat/customer-completion` and `gh pr checks 14 --watch`. Expected: all PR checks pass.

- [ ] **Step 2: Confirm merge readiness.** Run `gh pr view 14 --json state,mergeStateStatus,url`. Expected: `state` is `OPEN` and `mergeStateStatus` is no longer `DIRTY`. Do not merge yet: the final whole-branch review is the required gate before `gh pr merge 14 --squash`.

### Final integration gate: Review and squash-merge

After Task 3 is approved, conduct the broad whole-branch review. Only with an approved review, run `gh pr merge 14 --squash`, then `gh pr view 14 --json state,mergedAt,mergeCommit,url`. Expected: state `MERGED`, a populated `mergedAt`, and no use of `--delete-branch`.

### Task 4: Correct CI-blocking commit subjects

**Files:**
- Rewrite: Git commits `43076f7` and `515eda8` on `origin/feat/customer-completion`
- Preserve: every commit tree, merge topology, and the current workspace's unstaged files

**Interfaces:**
- Consumes: the user-approved history rewrite, `origin/main` at `2424b22`, and PR head `aea52d5`.
- Produces: a lease-protected force-push whose only semantic history change is two lowercase conventional-commit subjects.

- [ ] **Step 1: Back up before rewriting.** In `/tmp`, create a Git bundle of `feat/customer-completion` and a checksum-backed archive of the three modified devcontainer files plus this untracked plan. Confirm the local and remote PR heads both equal `aea52d5` before proceeding.

- [ ] **Step 2: Rewrite only the linear PR subjects in a disposable clone.** Clone only `feat/customer-completion`, fetch `main`, and interactive-rebase exactly the linear `1451bce..ae6da23` PR chain onto unchanged `1451bce`. Use fail-closed temporary editors to preserve message bodies while replacing only these subjects: `43076f7` → `feat(events): add customer-created integration event contract`; `515eda8` → `feat(customer): add customer and address domain`. Re-merge the exact signed `origin/main` commit (`2424b22`) and resolve its sole conflict with the rewritten PR's `Program.cs`, then cherry-pick `aea52d5` without `-x`.

- [ ] **Step 3: Verify the isolated rewrite.** Compare the eleven rewritten linear commits' ordered tree IDs and message bodies to their originals; only the two requested subjects may differ. Confirm the recreated merge tree equals `cc15cb2^{tree}`, its second parent is exactly `2424b22`, and the final tree equals `aea52d5^{tree}`.

- [ ] **Step 4: Force-push safely and synchronize the local ref.** Recheck the remote head, then force-push only with `--force-with-lease=refs/heads/feat/customer-completion:aea52d5`. Fetch the new remote head into the current workspace and move its local branch ref with a guarded `git update-ref`; do not modify the working tree. Verify the archive checksums and status after synchronization.

### Task 5: Restore reviewed Gitleaks fixture entries after the rewrite

**Files:**
- Modify: `.gitleaksignore:5-6`
- Validate: `tests/unit/Customer.UnitTests/Customer.UnitTests.csproj`

**Interfaces:**
- Consumes: the existing reviewed `generic-api-key` fixture exceptions and the rewritten commit IDs `694532522333db68e47b545af9c1791d8b1b8187` and `d2b0cbaf4ff2501af829c6f970f1ed6e2978a159`.
- Produces: the same two narrowly scoped test-fixture exceptions, matched to the rewritten history so the full-history CI scan succeeds.

- [ ] **Step 1: Reproduce the Gitleaks failure.** Run `./tools/security-scan.sh --secrets`; expect `generic-api-key` findings for the two `keycloak-sub-1` test fixtures because the current allowlist still names their pre-rewrite commit IDs.

- [ ] **Step 2: Update only the two rewritten IDs.** Replace the commit IDs in `.gitleaksignore` lines 5-6 with the exact rewritten IDs. Do not add a new rule, path, secret pattern, or allowlist entry; retain the existing reviewed-false-positive comments.

- [ ] **Step 3: Verify and commit.** Run `dotnet test tests/unit/Customer.UnitTests/Customer.UnitTests.csproj`, `./tools/security-scan.sh`, and `git diff --check`. Commit only `.gitleaksignore` as `fix(security): update customer fixture allowlist`.

- [ ] **Step 4: Push and recheck CI.** Push the commit, wait for `gh pr checks 14 --watch`, and confirm the PR is open and no longer `UNSTABLE` before the final whole-branch review.
