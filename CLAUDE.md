# wms.pg-rest.app - notes for agents

Public repository (`kldev/wms.pg-rest.app`). PostgreSQL 18.6 + PostgREST (API generated from the `api` schema) +
Blazor .NET 10 (Interactive Server). Goals and decisions: `PRODUCT.md`; requirements: `docs/requirements/requirements.md`;
domain: `docs/domain.md`.

Rules:
- The API is PostgREST. No ASP.NET controllers or endpoints for domain operations, no EF Core, no Npgsql in the
  Blazor host - it calls PostgREST over HTTP only.
- Tables in schema `data`; schema `api` holds only views and functions, granted to `web_anon` / `app_user`.
  Rules (stock >= 0, unique codes) are enforced in the database.
- PostgREST connects as `authenticator` only (NOINHERIT, no superuser, owns nothing).
- Public repo: no secrets (only `infrastructure/.env-sample` placeholders), no personal data, no real company names,
  no private addresses (LAN IPs, internal host names). `.instructions/` is local and git-ignored.
- Push only when the owner asks. Conventional commits, small commits that build.
- `TreatWarningsAsErrors`, central package versions, CSharpier, `scripts/inspect-code.sh` must report nothing.
- Styles: Tailwind v4 from `src/PgRestWms.Web/Styles/app.css`; `wwwroot/app.css` is generated and committed -
  rebuild with `./scripts/tailwind.sh` after changing markup classes. Keep class names whole in `.razor` files.
- The JWT never goes to the browser: it lives in `SessionTokens` (server memory), the cookie holds a session id.
- Spec-driven: each spec (S01...) has acceptance criteria in the owner's SDD project; `PRODUCT.md` mirrors it.
