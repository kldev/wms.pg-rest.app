# How PostgREST works here

[PostgREST](https://github.com/PostgREST/postgrest) (MIT, v16.5 in `infrastructure/docker-compose.yml`) is a single
binary that reads the PostgreSQL catalog and serves one or more schemas as a REST API. There is no API code in this
repository: the "backend" is `db/init/020-api.sql`. This file explains what happens between an HTTP request and the
database, using this application's objects.

## The picture

```mermaid
sequenceDiagram
    participant B as Blazor host
    participant P as PostgREST
    participant D as PostgreSQL
    B->>P: POST /rpc/issue  Authorization: Bearer <JWT>  {"product_id":..., "quantity": 6}
    P->>P: verify the JWT (HS256, JWT_SECRET), read the "role" claim
    P->>D: BEGIN (connection of authenticator, from the pool)
    P->>D: SET LOCAL ROLE app_user; set_config('request.jwt.claims', '{"sub":"demo",...}', true)
    P->>D: SELECT api.issue(product_id := ..., warehouse_id := ..., quantity := 6, note := NULL)
    D-->>P: ERROR PT409 "Not enough stock: 5 available" (HINT quantity)
    P->>D: ROLLBACK
    P-->>B: 409 {"code":"PT409","message":"Not enough stock: 5 available","hint":"quantity"}
```

Every request is one transaction, run as the role the token names. PostgREST itself has no rights: it logs in as
`authenticator` and can only switch to the roles granted to it.

## What becomes an endpoint

PostgREST serves only the schemas in `PGRST_DB_SCHEMAS` (`api` here), and from them only what the caller's role may
use. It reads tables, views, functions, keys and comments once into a **schema cache**.

| In schema `api` | HTTP | Here |
|---|---|---|
| a view or table | `GET /name`; `POST`, `PATCH`, `DELETE` when the view is updatable **and** granted | `products`, `warehouses` (simple views over one table, so PostgreSQL makes them updatable; `INSERT`/`UPDATE` granted on three columns, no `DELETE`) |
| a read-only view | `GET /name` | `stock`, `movements` (joins) |
| a function | `POST /rpc/name` with a JSON object whose keys are the **argument names**; `GET` too for `STABLE`/`IMMUTABLE` | `login`, `me`, `receive`, `issue` |
| `COMMENT ON ...` | description in the OpenAPI document | every view and function |

Tables stay in schema `data`, which PostgREST never sees ("schema isolation"). Views run with their owner's rights
(`wms_owner`), so a client needs a grant on the view only - never on `data`.

A function is found by name **and** argument names: `POST /rpc/receive {}` is a 404 ("no function matches"), not a
permission error, because no `receive()` without arguments exists.

## Reading: the URL is the query

| URL | SQL it becomes (roughly) |
|---|---|
| `/products?sku=eq.BOLT-M8` | `WHERE sku = 'BOLT-M8'` |
| `/stock?or=(sku.ilike."*bolt*",product_name.ilike."*bolt*")` | `WHERE sku ILIKE '%bolt%' OR product_name ILIKE '%bolt%'` |
| `/movements?kind=eq.issue&order=created_at.desc,id.asc` | `WHERE kind = 'issue' ORDER BY created_at DESC, id` |
| `/products?select=sku,name&limit=20&offset=40` | `SELECT sku, name ... LIMIT 20 OFFSET 40` |

Headers do the rest:

| Header | Effect |
|---|---|
| `Prefer: count=exact` | total rows of the filter in `Content-Range: 0-19/57` (the pager in Blazor uses it) |
| `Prefer: return=representation` | `POST`/`PATCH` return the written row (the generated `id`, the normalised `sku`) |
| `Accept: application/vnd.pgrst.object+json` | one object instead of an array of one |

Values are parameters, never pasted into SQL; column names must exist in the schema cache. In Blazor,
`PostgRest/PostgRestQuery.cs` builds these URLs from code-defined columns and escapes the values (`or=(...)` needs
double quotes around values with `,.:()`).

## Writing: rules live in the database

`api.receive` and `api.issue` are `plpgsql`, `SECURITY DEFINER` (they run as `wms_owner`, never as a superuser) and
`SET search_path = ''`. `issue` locks the stock row (`SELECT ... FOR UPDATE`), so two concurrent issues of the same
product in the same warehouse queue up; the test fires ten issues of 3 at a stock of 10 and exactly three succeed.
Unique codes, allowed units and the SKU format are table constraints; upper-casing is a trigger. Nothing of this is
in C#.

## Errors: SQLSTATE becomes the HTTP status

PostgREST passes the database error on as JSON `{code, message, details, hint}` and picks the status from the code:

| Raised by | `code` | HTTP |
|---|---|---|
| no token, and `web_anon` lacks the grant | `42501` | 401 |
| a signed-in role lacks the grant (delete a product, write to `stock`) | `42501` | 403 |
| `RAISE invalid_password` in `api.login` | `28P01` | 403 |
| bad or expired JWT | `PGRST301`/`PGRST303` | 401 |
| unique violation (`products_sku_key`) | `23505` | 409 |
| check violation (`products_sku_check`) | `23514` | 400 |
| `RAISE invalid_parameter_value` (quantity <= 0) | `22023` | 400 |
| `RAISE SQLSTATE 'PT404'` / `'PT409'` | `PTxyz` | xyz - any status you choose |

The functions put the argument's name into `HINT`; the Blazor forms (`PostgRest/PostgRestException.cs`) show the
message next to that field, or map a known constraint name to a field.

## Signing in: a JWT made by the database

1. `POST /rpc/login {"username","password"}` - the only call `web_anon` may make.
2. `api.login` checks the bcrypt hash (`pgcrypto` `crypt`) and builds an HS256 JWT in SQL: `data.jwt_sign` = base64url
   of header and claims, signed with `pgcrypto` `hmac(..., 'sha256')`. Claims: `role` (`app_user`), `sub`
   (username), `exp`. The usual `pgjwt` extension is not in the alpine image, so this is about fifteen lines of SQL.
3. The key is `JWT_SECRET` from `.env`: PostgREST gets it as `PGRST_JWT_SECRET` to verify, the database keeps a copy in
   `data.settings` (readable by `wms_owner` only) to sign.
4. On each request PostgREST verifies the signature and `exp`, then `SET LOCAL ROLE app_user` and puts the claims in
   `request.jwt.claims`; `data.current_username()` reads `sub` from there for `movements.created_by`.

The Blazor host keeps the token in its memory; the browser only has a session cookie (`src/PgRestWms.Web/Auth/`).

## Changing the API

Change a view or function in `api`, then tell PostgREST to reload its schema cache - no restart:

```sql
NOTIFY pgrst, 'reload schema';
```

`SchemaReloadTests` adds a column to `api.warehouses` this way and sees it in `GET` and in the OpenAPI document.

## The OpenAPI document

`GET /` returns an OpenAPI description of what **the caller's role** may use - anonymous callers see only
`/rpc/login`. What it gives and what it does not (measured on this API):

| | |
|---|---|
| format | Swagger **2.0** |
| tables, views | a schema per view with column types (`uuid`, `text`, `numeric` -> `number`), descriptions from comments |
| functions | the request body (argument names, types, which are required); the response only as "200 OK" - a function returning `jsonb` has no response schema |
| errors | none documented: responses list only 200, 201, 204, 206 |
| filters, `Prefer`, `Content-Range` | generic string parameters (16 on `GET /stock`), not the operators or the count |

That is enough to read and try the API, but thin for generating a typed client. The Blazor host therefore uses a
small hand-written client (`src/PgRestWms.Web/PostgRest/PostgRestClient.cs`, decision D16 in `PRODUCT.md`).

## Configuration

All through environment variables in `infrastructure/docker-compose.yml`:

| Variable | Here | |
|---|---|---|
| `PGRST_DB_URI` | `postgres://authenticator:...@db:5432/wms` | the only login PostgREST has |
| `PGRST_DB_SCHEMAS` | `api` | what becomes the API |
| `PGRST_DB_ANON_ROLE` | `web_anon` | role for requests without a token |
| `PGRST_JWT_SECRET` | `${JWT_SECRET}` | HS256 key, at least 32 characters |
| `PGRST_OPENAPI_SERVER_PROXY_URI` | `http://127.0.0.1:6501` | the address written into the OpenAPI document |
| `PGRST_ADMIN_SERVER_PORT` / `_HOST` | `3001` / `localhost` | `/live` and `/ready`; the healthcheck runs `postgrest --ready`, which refuses the default host `!4` |

## Pitfalls we hit

- **Inlined SQL functions skip `EXECUTE` checks.** PostgreSQL inlines a simple `LANGUAGE sql` function into the query
  plan and checks `EXECUTE` only while planning. PostgREST keeps prepared statements on its pooled connections, so after
  `app_user` had called `rpc/me` on a connection, anonymous calls on it got 200 instead of 401. Every function in `api`
  is now `plpgsql`, and `FunctionPrivilegeTests` checks both the behaviour and the catalog. Views are not affected:
  their privileges are checked at execution.
- **A non-updatable view answers a write with 500.** PostgreSQL reports "cannot insert into view" (`55000`) before it
  checks privileges. `stock` and `movements` have `INSTEAD OF` triggers that raise, so the privilege check comes first
  and the client gets 403.
- **`/rpc/...` matches argument names.** A wrong body is a 404 before any permission check.
- **Init scripts run once.** `db/init/` runs only on an empty volume; on a live database apply the change with `psql`
  and `NOTIFY pgrst`, and put it into `db/init/` for the next fresh start.
