# tests/ — Testing Conventions

## Structure

| Directory | Framework | Scope |
|-----------|-----------|-------|
| `unit/` | xUnit (.NET), Bun test (TS) | Isolated logic, no I/O |
| `integration/` | xUnit + Testcontainers | Real DB/MQ, service-level |
| `architecture/` | ArchUnitNET | Layer boundary enforcement |

## Conventions (from Teck.Cloud)

- **Naming**: `Method_WhenCondition_ExpectedResult`
- **Structure**: Arrange-Act-Assert
- **Integration tests**: prefer real dependencies via Testcontainers over mocking
- **Architecture tests**: enforce clean architecture boundaries (API must not contain Request/Validator types, Application must not contain endpoint types)

## Running

```bash
nx test                              # all tests
nx affected -t test                  # only changed projects
nx test --project=catalog-api        # single project
```
