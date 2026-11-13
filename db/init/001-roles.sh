#!/bin/sh
# Roles and schemas for PostgREST. Runs once, on an empty volume (docker-entrypoint-initdb.d), as the superuser.
#   authenticator  LOGIN NOINHERIT - the only role PostgREST connects with; it owns nothing and has no rights itself,
#                  it can only switch (SET ROLE) to web_anon or app_user for a request.
#   web_anon       requests without a token
#   app_user       signed-in users (the `role` claim of the JWT)
# Tables live in the private schema `data`; PostgREST exposes only `api` (views and functions).
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v authenticator_password="$AUTHENTICATOR_PASSWORD" <<'SQL'
CREATE ROLE authenticator LOGIN NOINHERIT NOCREATEDB NOCREATEROLE NOSUPERUSER PASSWORD :'authenticator_password';
CREATE ROLE web_anon NOLOGIN;
CREATE ROLE app_user NOLOGIN;
GRANT web_anon TO authenticator;
GRANT app_user TO authenticator;

REVOKE ALL ON SCHEMA public FROM PUBLIC;
REVOKE ALL ON DATABASE wms FROM PUBLIC;
GRANT CONNECT ON DATABASE wms TO authenticator;

CREATE SCHEMA data;
CREATE SCHEMA api;
GRANT USAGE ON SCHEMA api TO web_anon, app_user;
SQL
