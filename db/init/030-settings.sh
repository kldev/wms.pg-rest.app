#!/bin/sh
# Settings from the environment (infrastructure/.env): the JWT key and lifetime, and the application's user.
# Runs once, on an empty volume. The JWT secret stays in data.settings, readable only by wms_owner.
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v jwt_secret="$JWT_SECRET" -v jwt_lifetime="${JWT_LIFETIME:-8 hours}" \
  -v app_username="$APP_USERNAME" -v app_password="$APP_PASSWORD" <<'SQL'
SET ROLE wms_owner;
INSERT INTO data.settings (jwt_secret, jwt_lifetime) VALUES (:'jwt_secret', :'jwt_lifetime'::interval);
INSERT INTO data.users (username, password_hash)
VALUES (lower(:'app_username'), ext.crypt(:'app_password', ext.gen_salt('bf', 10)));
SQL
