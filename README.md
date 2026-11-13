# wms.pg-rest.app

A mini warehouse (products, warehouses, stock, receipts, issues) where the REST API is **not written in C#**:
[PostgREST](https://github.com/PostgREST/postgrest) generates it from the PostgreSQL schema. The front is
**Blazor on .NET 10**. Product goals and decisions: [`PRODUCT.md`](PRODUCT.md).

```
browser ──► Blazor (Interactive Server, :6500) ──HTTP + JWT──► PostgREST (:6501) ──► PostgreSQL 18.6 (:6502)
                                                               api schema = the API   data schema = tables
```

| Component | Version |
|---|---|
| PostgreSQL | `postgres:18.6-alpine` |
| PostgREST | `postgrest/postgrest:v16.5` |
| .NET SDK | 10.0.401 (`global.json`) |

## Run

Requirements: Docker with Compose v2.

```bash
cd infrastructure
./start.sh --init-env      # .env with random secrets (or: cp .env-sample .env)
./start.sh                 # build and start; waits until every container is healthy
```

| | Address |
|---|---|
| Web | http://127.0.0.1:6500 (health: `/health`) |
| PostgREST | http://127.0.0.1:6501 (OpenAPI 2.0 at `/`) |
| PostgreSQL | `127.0.0.1:6502`, database `wms`, user `postgres` (password in `infrastructure/.env`) |

`./start.sh --help` lists the other commands (`--logs`, `--stop`, `--clean`, ...).

The SQL in `db/init/` runs only when the database volume is empty. After changing it: `./start.sh --clean && ./start.sh`
(this deletes the local data).

## The API

Everything in the `api` schema is the API; tables live in `data` and are never exposed. `db/init/020-api.sql`
is the whole "backend".

| Request | What |
|---|---|
| `POST /rpc/login` `{"username","password"}` | JWT (`token`) for `Authorization: Bearer` - the only call allowed without it |
| `GET /products`, `/warehouses`, `/stock`, `/movements` | lists - PostgREST filters (`?sku=like.BOLT*`), `order`, `limit`/`offset`, `Prefer: count=exact` |
| `POST`, `PATCH /products`, `/warehouses` | master data (no delete) |
| `POST /rpc/receive`, `/rpc/issue` `{"product_id","warehouse_id","quantity","note"}` | the only way to change stock |
| `POST /rpc/me` | the signed-in user |

```bash
cd infrastructure && set -a && . ./.env && set +a
TOKEN=$(curl -s http://127.0.0.1:6501/rpc/login -H 'Content-Type: application/json' \
  -d "{\"username\":\"$APP_USERNAME\",\"password\":\"$APP_PASSWORD\"}" | jq -r .token)
curl -s 'http://127.0.0.1:6501/stock?select=sku,warehouse_code,quantity&order=sku' -H "Authorization: Bearer $TOKEN"
```

Rules live in the database and come back as PostgREST errors (`code`, `message`, `details`, `hint`):

| Case | HTTP | `code` |
|---|---|---|
| no or invalid token | 401 | `42501` / `PGRST301` |
| wrong username or password | 403 | `28P01` |
| writing what the role may not (delete, write to `stock`) | 403 | `42501` |
| duplicate SKU or warehouse code | 409 | `23505` |
| invalid value (SKU format, unit) | 400 | `23514` |
| quantity <= 0 | 400 | `22023` |
| unknown product or warehouse | 404 | `PT404` |
| issue more than the stock | 409 | `PT409` (`Not enough stock: 5 available`) |

The OpenAPI document at `/` lists what the caller's role may use: without a token only `rpc/login`.

### Add something to the API

1. Add a view or function to `api` (with `SET ROLE wms_owner`) and grant it to `app_user`.
2. On a running database run the same SQL with `psql`, then `NOTIFY pgrst, 'reload schema';` - PostgREST serves it
   at once, no restart and no C# change (`SchemaReloadTests` shows it).
3. Put the SQL into `db/init/` so a fresh volume gets it too.

Roles: PostgREST logs in as `authenticator` (no rights of its own) and switches to `web_anon` (no token) or
`app_user` (the JWT's `role`). Everything is owned by `wms_owner`; `SECURITY DEFINER` functions run as it, never as a
superuser. The JWT is HS256, signed in SQL with `pgcrypto` (`data.jwt_sign`); its key is `JWT_SECRET` in `.env`,
shared by the database and PostgREST.

## Develop

```bash
dotnet restore             # also restores the local tools (CSharpier, Husky.Net) and installs the git hooks
dotnet build PgRestWms.slnx
dotnet test --solution PgRestWms.slnx                                # needs Docker (Testcontainers)
dotnet test --solution PgRestWms.slnx --filter-class '*.StockTests'  # one class
dotnet csharpier format .
```

API tests (`tests/PgRestWms.Tests/Api/`) start PostgreSQL 18.6 with `db/init` and PostgREST in Docker, with random
secrets, and call the API over HTTP.

Run the Blazor host outside Docker: `./infrastructure/start.sh -s web`, then
`PostgRest__BaseUrl=http://127.0.0.1:6501 dotnet run --project src/PgRestWms.Web`.

Git hooks (Husky.Net): pre-commit - `csharpier check`, build; pre-push - build, `scripts/inspect-code.sh`
(JetBrains inspection, needs `dotnet tool install -g JetBrains.ReSharper.GlobalTools`). `HUSKY=0` turns them off.

## Layout

| Path | What |
|---|---|
| `db/init/` | roles, schemas, tables, `api` views and functions (run in name order on an empty volume) |
| `src/PgRestWms.Web/` | Blazor host |
| `tests/PgRestWms.Tests/` | tests |
| `infrastructure/` | `docker-compose.yml`, `.env-sample`, `start.sh` |
| `docs/` | requirements, domain |

## License

[MIT](LICENSE)
