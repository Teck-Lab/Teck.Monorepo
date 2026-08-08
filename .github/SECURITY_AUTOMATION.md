# Security automation

Security findings converge in GitHub Code Scanning, Dependabot, or Secret
Scanning and are synchronized into issues and the `Teck Scrum` Project by the
security alert intake workflow.

The Project's Scrum `Priority` field remains a human product decision. Intake
records a recommended security priority in the issue and fills the technical
EPSS and KEV Project fields instead of overwriting product priority.

Verified Dependabot pull requests are added to `Teck Scrum`. Security pull
requests are also correlated to their tracked issues by advisory and package
metadata, receive native `Closes #...` links, and move the tracked issue to
`agent:in-review`. Version-update pull requests without a security advisory do
not create or claim agent work. Approval and merge remain human decisions.
The scheduled security intake owns issue creation; PR reconciliation never
runs intake in parallel. Intake also keeps the oldest issue as the canonical
record for each alert fingerprint and closes later duplicates during recovery.

## Scan stages

- Pull requests: CodeQL default queries, Semgrep, zizmor, Trivy configuration,
  dependency review, Gitleaks, builds, tests, and architecture tests.
- Main and weekly: CodeQL `security-extended`, source dependency scans, SBOM
  submission, and all pull-request scanners.
- Canary and release images: Trivy image scan, SARIF upload, SBOM/VEX creation,
  signing, and verification. Releases fail on fixable high or critical image
  vulnerabilities; previews report them.
- Deployed environments: `container-rescan.yml` accepts the immutable image
  digest matrix from Teck.GitOps. The GitOps schedule should call it for the
  exact digests deployed in each environment.
- Deployed previews: `preview-dast.yml` accepts the HTTPS preview base URL and
  OpenAPI schema URL from Teck.GitOps after rollout. It refuses hostnames that
  do not look like preview, canary, development, or staging environments.

Example cross-repository calls from Teck.GitOps:

```yaml
jobs:
  container-security:
    uses: Teck-Lab/Teck.Monorepo/.github/workflows/container-rescan.yml@main
    with:
      images: ${{ needs.discover.outputs.image_matrix }}
    secrets:
      registry-token: ${{ secrets.GITHUB_TOKEN }}

  preview-dast:
    uses: Teck-Lab/Teck.Monorepo/.github/workflows/preview-dast.yml@main
    with:
      base-url: ${{ needs.deploy.outputs.preview_url }}
      schema-url: ${{ needs.deploy.outputs.openapi_url }}
      report-only: true
    secrets:
      api-authorization: ${{ secrets.PREVIEW_API_AUTHORIZATION }}
```

Move DAST from report-only to blocking after its baseline has been reviewed.
Never point the DAST workflow at production.

## Application security invariants

Security-sensitive features must include deterministic tests for the relevant
invariants: unauthenticated denial, authorization boundaries, tenant mismatch
and cross-tenant isolation, trusted-header stripping, service-token exchange,
webhook signature and replay protection, amount calculation on the server,
idempotency, rate limits, and SSRF-safe outbound requests. Only applicable
invariants are required; scanners do not replace these tests.
