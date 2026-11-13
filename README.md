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

## Develop

```bash
dotnet restore             # also restores the local tools (CSharpier, Husky.Net) and installs the git hooks
dotnet build PgRestWms.slnx
dotnet test --solution PgRestWms.slnx
dotnet csharpier format .
```

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
