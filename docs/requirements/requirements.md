# Requirements (stage 1)

- **Date:** 2026-11-13
- **Source:** the owner's request: a simple API + front application where the API is not written in C# but generated
  from the PostgreSQL schema by an open-source tool (PostgREST), the front is Blazor on .NET 10, the database is
  PostgreSQL 18.6 (alpine image), and the project is published as a public GitHub repository.
- **Status:** draft for the Shape gate. Decisions D1–D12 are the owner's answers of 2026-11-13; items marked
  *proposal* wait for acceptance.

## Decisions (2026-11-13)

| # | Decision |
|---|---|
| D1 | **PostgreSQL 18.6** from `postgres:18.6-alpine`. |
| D2 | **PostgREST** (MIT, https://github.com/PostgREST/postgrest) generates the REST API and OpenAPI from the `api` schema, image `postgrest/postgrest` pinned to a release tag (v16.5 at the time of writing). **No own API generator, no ASP.NET controllers for domain operations, no EF Core.** |
| D3 | **Blazor Web App on .NET 10**, Interactive Server render mode. The browser talks only to Blazor; Blazor calls PostgREST from the server, so the JWT never reaches the browser and no CORS is needed. |
| D4 | Domain: a **mini warehouse** - products, warehouses, stock levels, goods receipt and goods issue. Not a full WMS. |
| D5 | Language of the repository, UI and error messages: **English**. |
| D6 | Sign-in with **JWT**: a `login` function in the database checks the password and returns a token; PostgREST verifies it and switches to the role in the `role` claim. |
| D7 | UI: **Tailwind CSS with own Blazor components** (no MudBlazor, Radzen, FluentUI, Bootstrap). |
| D8 | Local run with **docker compose** (`infrastructure/`); no Kubernetes, Helm, RabbitMQ, Redis, SSO, OpenTelemetry collector. |
| D9 | Public repository: MIT licence, copyright "wms.pg-rest.app contributors", no secrets, personal data, real company names or private infrastructure addresses. Push only on the owner's request. |
| D10 | Migrations: numbered SQL scripts run by the `postgres` image on an empty volume (`docker-entrypoint-initdb.d`). Reset = `docker compose down -v`. |
| D11 | CI: GitHub Actions - build, tests (on Testcontainers). |
| D12 | Spec-driven development: SDD project in the owner's local wiki, `PRODUCT.md` mirrors it, one agent prompt per spec in `.instructions/` (local, git-ignored). |
| D13 | Ports on `127.0.0.1`: web 6500, PostgREST 6501, PostgreSQL 6502 (configurable in `.env`). |
| D14 | Token lifetime from `.env` (`JWT_LIFETIME`, default 8 h), passed to the database with the secret. |
| D16 | The PostgREST client in the Blazor host is written by hand (no Kiota/NSwag): PostgREST's OpenAPI 2.0 does not describe filters, `Prefer` or `Content-Range`, which the lists depend on. (2026-11-13, S03) |
| D17 | The JWT is kept in the host's memory under a random session id; the cookie carries the id and the username only. A host restart ends the sessions. (2026-11-13, S03) |
| D15 | Repository: `kldev/wms.pg-rest.app` on GitHub, public. The surrounding local repository ignores the directory. |

## Requirements

### R1 Repository and local stack (spec S01)

1. .NET 10 repository: `PgRestWms.slnx`, `global.json` (SDK 10.0.401, `latestFeature`), `Directory.Build.props`
   (`TreatWarningsAsErrors`, nullable), `Directory.Packages.props` (central versions), CSharpier and Husky.Net
   pre-commit, `.editorconfig`, `.gitignore` (with `.env` and `.instructions/`), `.dockerignore`, `README.md`,
   `LICENSE` (MIT).
2. Layout: `db/` (SQL), `src/PgRestWms.Web/` (Blazor), `tests/`, `infrastructure/` (`docker-compose.yml`,
   `.env-sample`, `start.sh`), `docs/`.
3. Compose services: `db` (`postgres:18.6-alpine`, volume on `/var/lib/postgresql`, healthcheck `pg_isready`),
   `api` (PostgREST, configuration only through `PGRST_*`, admin server with `/ready` as healthcheck, starts after
   `db` is healthy), `web` (Blazor from the repository's multi-stage, non-root `Dockerfile` with `HEALTHCHECK` on
   `/health`).
4. Ports bound to `127.0.0.1` only, numbers in `.env`: web 6500, PostgREST 6501, PostgreSQL 6502 (D13).
5. `cp infrastructure/.env-sample infrastructure/.env && ./infrastructure/start.sh` brings up the stack from a fresh
   clone; `.env-sample` explains how to generate the passwords and the JWT secret (≥ 32 characters).

### R2 Database and API through PostgREST (spec S02)

1. Tables in the private schema `data`; the `api` schema (the only one exposed, `PGRST_DB_SCHEMAS=api`) holds views
   and functions only ("schema isolation").
2. Roles: `authenticator` (`LOGIN NOINHERIT`, the only role PostgREST connects with; never a superuser or table
   owner), `web_anon` (can only call `api.login`), `app_user` (signed-in user). `GRANT` only on objects in `api`.
3. Users in `data.users` with a password hash (`pgcrypto` `crypt` + `gen_salt('bf')`). `api.login(username,
   password)` returns `{ token }`; the token is HS256, signed in SQL with `pgcrypto` `hmac` (the `pgjwt` extension is
   not in the alpine image), claims `role`, `sub` (username), `exp` (lifetime from `.env`, default 8 h, D14). Wrong credentials →
   403 (`28P01`), the same message for a wrong user and a wrong password.
4. The JWT secret is one value from `.env`, given to PostgREST (`PGRST_JWT_SECRET`) and to the database (setting read
   by `api.login`) at initialisation; it is never in the repository.
5. Domain (D4):
   - products: SKU (unique, upper-case), name, unit; create, edit, list with filter/sort/paging;
   - warehouses: code (unique), name; create, edit, list;
   - stock: quantity per product and warehouse, read-only for clients;
   - movements: receipt or issue, product, warehouse, quantity (> 0), note, who and when; read-only history.
   - `api.receive(...)` and `api.issue(...)` are the only way to change stock; both write a movement and the stock
     in one transaction; issue locks the stock row and fails when the quantity is not enough (stock never < 0).
6. Rules in the database (`NOT NULL`, `CHECK`, `UNIQUE`, foreign keys, `RAISE` with a readable message and a hint);
   PostgREST returns them as JSON (`code`, `message`, `details`, `hint`).
7. Demo data (invented) in a separate script that can be skipped; a demo user from `.env`.
8. README explains `NOTIFY pgrst, 'reload schema'` after changing the `api` schema.

### R3 Blazor front (spec S03)

1. Blazor Web App, Interactive Server, Tailwind CSS (standalone CLI, no Node in the repository *proposal*), own
   components (button, input, table, pager, alert, dialog).
2. Typed `HttpClient` to PostgREST through `IHttpClientFactory`, base address from configuration
   (`PostgRest__BaseUrl`), token of the signed-in user added to each call. Hand-written client *proposal* (PostgREST
   OpenAPI is Swagger 2.0 and its RPC/filter surface fits generators poorly - check Kiota/NSwag before deciding).
3. Pages: Sign in, Stock (filter, sort, paging), Products (list + create/edit), Warehouses (list + create/edit),
   Receive / Issue form, Movements (history). Database errors shown next to the field or as a message.
4. Cookie authentication in Blazor; the PostgREST token kept on the server side of the session, never in the
   browser; sign-out.
5. `/health` endpoint in the Blazor host.

### R4 Publication (spec S04, only on the owner's request)

1. README: what it is, diagram (Blazor → PostgREST → PostgreSQL), how to run, how to add a table/function and see it
   in the API, component versions, screenshot.
2. GitHub Actions: build + tests.
3. Before the first push: scan of the whole history for secrets and private data; the owner approves the account,
   the repository name and the file list.

## Requirement → spec

| Requirement | Spec |
|---|---|
| R1.1–R1.5 | S01 |
| R2.1–R2.8 | S02 |
| R3.1–R3.5 | S03 |
| R4.1–R4.3 | S04 |

## Open questions

None (Q1–Q3 answered on 2026-11-13: D13–D15).
