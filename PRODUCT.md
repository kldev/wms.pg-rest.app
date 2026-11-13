# wms.pg-rest.app

A small warehouse application (products, warehouses, stock, receipts and issues) for developers who want to see a
working example of an API that is **generated from the PostgreSQL schema by PostgREST** instead of written in C#,
with a **Blazor .NET 10** front on top. The rules (stock never below zero, unique codes, who booked what) live in the
database; PostgREST exposes them as REST + OpenAPI; Blazor only displays and calls them. Requirements:
`docs/requirements/requirements.md`, domain: `docs/domain.md`. This file mirrors the project's SDD record.

State: **MVP** - S01 done (skeleton and compose stack); S02 next.

## What it proves

1. **The API needs no hand-written backend code.**
   Done when: every read and write the front uses is a view or function in the `api` schema served by PostgREST; a new
   column in a view appears in `GET` and in the OpenAPI after `NOTIFY pgrst, 'reload schema'` without touching C#.
   `dotnet test` (API tests on Testcontainers).
2. **The database enforces the rules and the security.**
   Done when: issuing more than the stock is refused by the database, anonymous requests cannot read or write
   anything but `login`, PostgREST connects as a non-superuser `authenticator`. `dotnet test --filter Api`.
3. **One command runs it from a fresh clone.**
   Done when: `cp infrastructure/.env-sample infrastructure/.env && ./infrastructure/start.sh` gives three healthy
   containers and the sign-in page at `http://127.0.0.1:6500`.

## Non-goals

Not ever:
- an own API generator or ASP.NET controllers for domain operations;
- EF Core or direct database access from Blazor;
- secrets or personal data in the repository.

Not yet:
- roles other than `app_user`, user management in the UI;
- batches, serial numbers, locations inside a warehouse, transfers, reservations, documents;
- deployment beyond docker compose, observability stack.

## Decisions

| # | Decision | Why | Revisit when |
|---|---|---|---|
| D1 | PostgreSQL 18.6 alpine | owner's choice | - |
| D2 | PostgREST generates the API | the point of the project | - |
| D3 | Blazor Interactive Server calls PostgREST from the server | token stays on the server, no CORS | a WebAssembly front is wanted |
| D4 | Mini-warehouse domain | small, but has a real rule (stock ≥ 0) | - |
| D5 | English | public repository | - |
| D6 | JWT from `api.login`, signed with `pgcrypto`, lifetime from `.env` | PostgREST-native auth; `pgjwt` not in the alpine image | the image gains `pgjwt` |
| D7 | Tailwind CSS + own components | owner's choice | - |
| D8 | docker compose only | simple local run | - |
| D9 | MIT, public, push on request | public repository | - |
| D10 | Init SQL scripts on an empty volume | simplest migrations | schema changes on kept data |
| D11 | GitHub Actions build + tests | public repository | - |
| D12 | Spec-driven development | owner's method | - |
| D13 | Ports 6500 web, 6501 PostgREST, 6502 PostgreSQL on 127.0.0.1 | free next to the author's other projects | - |
| D14 | Token lifetime from `.env` (`JWT_LIFETIME`, default 8 h) | owner's choice | - |
| D15 | Public repository `kldev/wms.pg-rest.app` | owner's choice | - |

## State

Works today:
- S01: `./infrastructure/start.sh` from a fresh clone brings up PostgreSQL 18.6, PostgREST v16.5 (connects as
  `authenticator`, serves the empty `api` schema and its OpenAPI) and the Blazor host (`/health`), all healthy, ports on
  127.0.0.1 only; `--init-env` generates random secrets;
- build with `TreatWarningsAsErrors`, CSharpier and JetBrains inspection clean, Husky.Net hooks, 1 test.

Next: S02 database and API, S03 Blazor front, S04 publication.
