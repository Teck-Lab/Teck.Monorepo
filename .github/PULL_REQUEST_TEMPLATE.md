## Summary

<!-- Describe the outcome and why this change is needed. -->

## Related work

<!-- Use "Closes #123" for the issue completed by this PR. Link parent/sub-issues when applicable. -->

Closes #

## Change type

- [ ] Fix or small feature
- [ ] Medium or larger feature
- [ ] Maintenance, dependency, or tooling change
- [ ] Security remediation

## Validation

<!-- List the exact automated and manual checks performed. -->

- [ ] Relevant build, test, lint, and type-check targets pass
- [ ] New or changed behavior has automated tests
- [ ] Tenant isolation and authorization boundaries were tested where applicable
- [ ] No secrets or sensitive data were added to source, logs, fixtures, or artifacts

Commands and evidence:

```text

```

## Risk and rollout

<!-- Describe migrations, compatibility, rollout/rollback, observability, and known limitations. Write N/A where appropriate. -->

## Preview and QA

- [ ] This is a fix/small feature and does not require a preview
- [ ] This is medium+ work; the test plan is documented and the `preview` label will be added for QA
- [ ] Preview is not applicable (explain below)

QA notes:

## Review checklist

- [ ] The title follows Conventional Commits (`type(scope): description`)
- [ ] The PR has one clear purpose and no unrelated changes
- [ ] Documentation and operational guidance are updated where needed
- [ ] Breaking changes and security-relevant behavior are called out explicitly
- [ ] No Git tags were created and `nx release` was not run
