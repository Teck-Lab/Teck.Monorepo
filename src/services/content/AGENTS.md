# src/services/content/ — Content Group

Stateless, standalone. Single service. Versioned as `content@{version}`.

## Services

| Service | Projects | Key Characteristics |
|---------|----------|-------------------|
| **image-generator** | Application + Host (2) | Stateless — no Domain, no database. Fire-and-forget image generation. |

## Structure

```
image-generator/
├── Image.Generator.Application/
│   ├── Features/
│   │   └── GenerateImage/V1/
│   └── Image.Generator.Application.csproj
├── Image.Generator.Host/
│   ├── Endpoints/
│   └── Image.Generator.Host.csproj
└── Directory.Build.props
```

## Rules

- **No domain model** — stateless service, no aggregate state
- **No database** — no EF Core, no migrations, no persistence
- **Fire-and-forget** — accepts requests, processes asynchronously, no synchronous coupling
