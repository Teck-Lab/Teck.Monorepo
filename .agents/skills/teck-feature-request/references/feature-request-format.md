# Teck feature-request format

Use this exact section order. Omit no required section. Link discovery artifacts
instead of copying them.

```markdown
## Outcome

<Who benefits, what changes for them, and why it matters.>

## Scope

<Product behavior included. Use domain language; avoid prescribing architecture.>

## Out of scope

<Adjacent behavior deliberately excluded. Write "None identified" when empty.>

## Product context

- Component: <Commerce | Operations | Content | Gateway | Web | Platform | Infrastructure>
- Priority: <Low | Medium | High | Urgent>
- Size: <Unknown | XS | S | M | L | XL>

## Acceptance criteria

- [ ] <Observable result stated without choosing an implementation.>

## Dependencies and related work

<Named Markdown links to Wayfinder maps, decisions, prototypes, research, or
other issues. Write "None identified" when empty.>

## Security and tenancy considerations

<Authentication, authorization, tenant isolation, secrets, payments, privacy,
or data-handling needs. Write "N/A" only after considering each category.>

## Ready for Orca

- [x] The product outcome, scope, exclusions, and acceptance criteria are clear
  enough for an engineering agent to begin planning.
```

## Draft quality gate

- Prefer a short product title prefixed with `feat:`.
- State user-visible or business behavior; do not invent files, classes,
  services, schemas, libraries, or task sequencing.
- Preserve uncertainty honestly. Use `Size: Unknown` rather than estimating
  from an incomplete design.
- Make each acceptance criterion independently observable and testable.
- Give every linked artifact a readable name; never present bare issue-number
  chains.
- A completed Wayfinder map may be linked as context, but its decision children
  remain discovery records and are never copied into the executable issue DAG.

## Publication gate

Before any GitHub mutation, show the exact title and body and obtain an explicit
yes to publishing that version. After creation, read the issue through GitHub
and reject the mutation as incomplete if it contains literal `\n`, a missing,
duplicate, empty, or out-of-order heading, the wrong repository, or no
`agent:ready` label when the readiness box is checked.
