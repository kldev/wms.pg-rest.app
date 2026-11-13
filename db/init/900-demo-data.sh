#!/bin/sh
# Invented demo data. Skipped with DEMO_DATA=false. Stock goes through api.receive / api.issue like any client's.
set -eu

if [ "${DEMO_DATA:-true}" = "false" ]; then
  echo "DEMO_DATA=false - no demo data"
  exit 0
fi

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v app_username="$APP_USERNAME" <<'SQL'
SET ROLE wms_owner;

INSERT INTO data.warehouses (code, name) VALUES
  ('MAIN', 'Main warehouse'),
  ('EAST', 'East depot');

INSERT INTO data.products (sku, name, unit) VALUES
  ('BOLT-M8', 'Hex bolt M8', 'pcs'),
  ('NUT-M8', 'Hex nut M8', 'pcs'),
  ('WASHER-8', 'Flat washer 8 mm', 'pcs'),
  ('CABLE-3X1.5', 'Power cable 3x1.5', 'm'),
  ('PAINT-WHITE', 'Wall paint, white', 'l'),
  ('CEMENT-25', 'Cement bag 25 kg', 'kg');

-- The movements are booked by the application's user, as if it had signed in.
SELECT set_config('request.jwt.claims', json_build_object('sub', lower(:'app_username'))::text, false);

SELECT api.receive(p.id, w.id, r.quantity, r.note)
FROM (VALUES
  ('BOLT-M8', 'MAIN', 500, 'Delivery 2026-11-13'),
  ('NUT-M8', 'MAIN', 500, 'Delivery 2026-11-13'),
  ('WASHER-8', 'MAIN', 1000, NULL),
  ('CABLE-3X1.5', 'EAST', 250, NULL),
  ('PAINT-WHITE', 'EAST', 80, NULL),
  ('CEMENT-25', 'MAIN', 1500, 'Pallets 1-3')
) AS r (sku, code, quantity, note)
JOIN data.products p ON p.sku = r.sku
JOIN data.warehouses w ON w.code = r.code;

SELECT api.issue(p.id, w.id, r.quantity, r.note)
FROM (VALUES
  ('BOLT-M8', 'MAIN', 120, 'Order 1001'),
  ('NUT-M8', 'MAIN', 120, 'Order 1001'),
  ('CABLE-3X1.5', 'EAST', 35.5, NULL)
) AS r (sku, code, quantity, note)
JOIN data.products p ON p.sku = r.sku
JOIN data.warehouses w ON w.code = r.code;
SQL
