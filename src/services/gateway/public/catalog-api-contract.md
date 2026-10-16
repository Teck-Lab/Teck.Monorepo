# Public Catalog product API contract

The public YARP gateway exposes only these product operations:

| Method | Path | Gateway route ID | Exchanged audience | Catalog permission | Contract |
|---|---|---|---|---|---|
| `GET` | `/products/{productId}` | `catalog-products-read` | `catalog-api` | `catalog:product-read` | Existing `ProductDto`, HTTP 200 |
| `POST` | `/products` | `catalog-products-write` | `catalog-api` | `catalog:manage` | Existing `CreateProductRequest` to `ProductDto`, HTTP 201 and `Location: /products/{id}` |

Catalog's protected-resource authorization is independently enforced at the service endpoint. `catalog:product-read` is granted to the local `platform-reader` and `platform-manager` roles; `catalog:manage` only to `platform-manager`. Both preserve the existing signed organization/tenant resolution and tenant-scoped repository filtering. Staff-only supplier reads use `catalog:supplier-read`; supplier mutation continues to require `catalog:manage`. The legacy `catalog:read` scope remains for list-products and is not granted by the new customer permission.

No list, category, variant mutation, supplier, cost, cart, checkout, or anonymous route is exposed through this gateway contract.
