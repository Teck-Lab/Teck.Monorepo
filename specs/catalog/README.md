# Catalog product OpenAPI contract

`products.openapi.json` is the deterministic, product-only projection of Catalog's live NSwag OpenAPI document. It contains only POST `/products` and GET `/products/{productId}` and only schemas reachable from those operations; supplier, procurement, product list, and staff-only variants are excluded.

With a local Catalog host exposing its OpenAPI endpoint at the default URL, update the checked document and generated client:

```sh
nx run openapi-tools:export:catalog
nx run api-client:generate
```

The live contract gate, `nx run openapi-tools:check:catalog-contract`, compares a normalized product-only OpenAPI projection from the running Catalog host with the checked spec, then compares generated TypeScript with the checked client. It requires Catalog OpenAPI at `http://127.0.0.1:5077/openapi/v1-public/openapi.json` or the `CATALOG_OPENAPI_URL` override.

For a different running Catalog endpoint, set `CATALOG_OPENAPI_URL` to its OpenAPI JSON URL. Never use this exporter to infer role permissions from `v1-public`: its public audience can include supplier operations. The explicit allowlist is the contract boundary. The gateway client must supply its authenticated base URL and credentials; no token or host is embedded in generated artifacts.
OAuth server URLs and runtime hosts are removed from the checked client contract; token acquisition stays with the gateway client’s caller.

Run the generated-client-only check with `nx run api-client:check:generated`; the live contract gate also performs this comparison.
