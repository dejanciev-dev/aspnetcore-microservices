# API reference

All endpoints are available through the **API gateway** at `http://localhost:5100`. Each service also serves its own interactive docs (Scalar) and OpenAPI document:

| Service | Interactive docs | OpenAPI document |
|---|---|---|
| Catalog | http://localhost:5101/scalar | http://localhost:5101/openapi/v1.json |
| Basket | http://localhost:5102/scalar | http://localhost:5102/openapi/v1.json |
| Ordering | http://localhost:5103/scalar | http://localhost:5103/openapi/v1.json |

- [Conventions](#conventions)
- [Authentication](#authentication)
- [Catalog](#catalog)
- [Basket](#basket)
- [Ordering](#ordering)
- [Errors](#errors)

---

## Conventions

- JSON uses **camelCase** property names.
- IDs are **GUIDs**.
- Money values are **decimals** (for example `129.99`).
- Errors are **RFC 9457 ProblemDetails** (`application/problem+json`); see [Errors](#errors).
- Every service exposes `GET /health`.

---

## Authentication

APIs accept **Keycloak access tokens** as `Authorization: Bearer <token>`. The token must be issued by the `storefront` realm and carry the `storefront-api` audience. The storefront itself never sends tokens: it uses the gateway's session cookie (BFF), and the gateway attaches the token.

| Endpoint | Access |
|---|---|
| `GET /bff/user` | public: `{ isAuthenticated, name, displayName, roles }` |
| `GET /bff/login?returnUrl=/path` | starts sign-in (redirect to Keycloak); only local return URLs are honoured |
| `GET /bff/logout` | ends the gateway session and the Keycloak SSO session |

Calls authenticated by the gateway session cookie must include `X-CSRF: 1`, otherwise the gateway returns `403`.

| Status | Meaning |
|---|---|
| `401 Unauthorized` | No token, or an invalid or expired one |
| `403 Forbidden` | Valid token, but missing the required role (e.g. `admin`), or a missing `X-CSRF` header |

---

## Catalog

### `GET /api/products`

Returns a page of products sorted by name.

| Query | Type | Default | Notes |
|---|---|---|---|
| `category` | string | – | Exact match, case-insensitive |
| `search` | string | – | Matches name or description |
| `page` | int | `1` | Minimum 1 |
| `pageSize` | int | `12` | Clamped to 1–100 |

```http
GET /api/products?category=Audio&pageSize=2
```

```json
{
  "items": [
    {
      "id": "4f1c9a0e-0001-4a6e-9b1a-000000000005",
      "name": "Noise-Cancelling Headphones",
      "description": "Over-ear, adaptive ANC, 30h playback.",
      "category": "Audio",
      "price": 249.50,
      "imageUrl": "https://picsum.photos/seed/headphones/480/320"
    }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 2,
  "totalPages": 1
}
```

### `GET /api/products/categories`

```json
["Accessories", "Audio", "Displays", "Peripherals"]
```

### `GET /api/products/{id}`

`200` with a product, or `404`.

### `POST /api/products`

Requires the **`admin`** role. Creates a product. Returns `201 Created` with a `Location` header.

```json
{
  "name": "Webcam",
  "description": "1080p webcam",
  "category": "Peripherals",
  "price": 79.99,
  "imageUrl": "https://example.com/webcam.png"
}
```

| Field | Rules |
|---|---|
| `name` | required, 2–100 characters |
| `description` | up to 1000 characters |
| `category` | required, up to 50 characters |
| `price` | 0.01 – 1,000,000 |
| `imageUrl` | optional, must be a valid URL |

### `PUT /api/products/{id}`

Requires **`admin`**. Same body as `POST`. A price change publishes `ProductPriceChanged` (see [architecture.md](architecture.md#messaging)). Returns `204` or `404`.

### `DELETE /api/products/{id}`

Requires **`admin`**. Returns `204` or `404`.

---

## Basket

All basket endpoints require a signed-in user and always act on **that user's** basket (the name comes from the token).

### `GET /api/basket`

Always `200`. A user without a basket gets an empty one.

```json
{
  "userName": "ada",
  "items": [
    {
      "productId": "4f1c9a0e-0001-4a6e-9b1a-000000000003",
      "productName": "4K Monitor",
      "imageUrl": "https://picsum.photos/seed/monitor/480/320",
      "unitPrice": 399.00,
      "quantity": 2,
      "lineTotal": 798.00
    }
  ],
  "total": 798.00
}
```

### `POST /api/basket/items`

Adds a product, or increases its quantity if it is already in the basket. **Name and price come from Catalog**, not from the request.

```json
{ "productId": "4f1c9a0e-0001-4a6e-9b1a-000000000003", "quantity": 2 }
```

| Field | Rules |
|---|---|
| `productId` | must exist in Catalog, otherwise `400` |
| `quantity` | 1–99, default `1` |

Returns `200` with the updated basket.

### `DELETE /api/basket/items/{productId}`

Removes one product. Returns `200` with the updated basket.

### `DELETE /api/basket`

Clears the basket. Returns `204`.

### `POST /api/basket/checkout`

Creates an order in Ordering from the current basket, then clears the basket.

```json
{
  "shippingAddress": {
    "fullName": "Ada Lovelace",
    "street": "1 Main St",
    "city": "London",
    "country": "UK",
    "zipCode": "N1 9GU"
  }
}
```

All address fields are required.

```json
{ "orderId": "c2b7a19c-c215-43e7-9aca-43ed5a5f1fd2", "total": 798.00 }
```

Returns `400` if the basket is empty.

> **Asynchronous when RabbitMQ is configured.** Basket generates `orderId`, publishes a `BasketCheckedOut` event and responds once the broker confirms. Ordering creates the order shortly afterwards, so `GET /api/orders/{orderId}` may return `404` for a moment; poll briefly. Without a broker, Basket calls `POST /api/orders` synchronously and the order exists when the response arrives.

---

## Ordering

All order endpoints require a signed-in user.

### `GET /api/orders`

The signed-in user's orders, newest first.

```json
[
  {
    "id": "c2b7a19c-c215-43e7-9aca-43ed5a5f1fd2",
    "userName": "ada",
    "items": [
      {
        "productId": "4f1c9a0e-0001-4a6e-9b1a-000000000003",
        "productName": "4K Monitor",
        "unitPrice": 399.00,
        "quantity": 2,
        "lineTotal": 798.00
      }
    ],
    "shippingAddress": {
      "fullName": "Ada Lovelace",
      "street": "1 Main St",
      "city": "London",
      "country": "UK",
      "zipCode": "N1 9GU"
    },
    "status": "Submitted",
    "createdAt": "2026-10-28T13:17:21.512+00:00",
    "total": 798.00
  }
]
```

`status` is one of `Submitted`, `Paid`, `Shipped`, `Cancelled`.

### `GET /api/orders/{id}`

`200` with the order if it belongs to the caller (or the caller is `admin`), otherwise `404`.

### `POST /api/orders`

Used by Basket's HTTP checkout fallback (with the caller's token relayed), but callable directly. The order is created for the **authenticated** user.

```json
{
  "items": [
    { "productId": "4f1c9a0e-0001-4a6e-9b1a-000000000003", "productName": "4K Monitor", "unitPrice": 399.00, "quantity": 2 }
  ],
  "shippingAddress": { "fullName": "Ada Lovelace", "street": "1 Main St", "city": "London", "country": "UK", "zipCode": "N1 9GU" }
}
```

| Field | Rules |
|---|---|
| `items` | required, at least one |
| `shippingAddress` | required, all fields required |

Returns `201 Created` with the order and a `Location` header.

---

## Errors

Every error uses the ProblemDetails format.

**Validation error (`400`)**

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "ProductId": ["Product '00000000-0000-0000-0000-000000000000' does not exist."]
  },
  "traceId": "00-b002531358411640d5eb2fd7a7498755-82ee5fbbd28bc94b-00"
}
```

**Not found (`404`)**

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "traceId": "00-17c0e1c37d99d7689cec1c9aff88b8c4-d1a97456da4ea068-01"
}
```

The `traceId` matches the distributed trace in the Aspire dashboard, so you can jump from an error response straight to its trace.

The storefront turns these responses into readable messages in [`problem-details.ts`](../src/Web/storefront/src/app/core/problem-details.ts).
