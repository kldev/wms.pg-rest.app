-- The API: everything in schema `api` is what PostgREST serves (views -> /<name>, functions -> /rpc/<name>).
-- Views run with the owner's rights (wms_owner), so clients need grants on the views only, never on `data`.
SET ROLE wms_owner;

------------------------------------------------------------------------------------------------------------------------
-- JWT (HS256) signed in SQL: pgcrypto's hmac. The pgjwt extension used by PostgREST's how-to is not in the alpine image.

CREATE FUNCTION data.base64url(bytes bytea) RETURNS text LANGUAGE sql IMMUTABLE STRICT AS $$
  SELECT translate(rtrim(replace(encode(bytes, 'base64'), E'\n', ''), '='), '+/', '-_')
$$;

CREATE FUNCTION data.jwt_sign(claims jsonb, secret text) RETURNS text LANGUAGE sql IMMUTABLE STRICT AS $$
  WITH unsigned AS (
    SELECT data.base64url(convert_to('{"alg":"HS256","typ":"JWT"}', 'UTF8')) || '.' ||
           data.base64url(convert_to(claims::text, 'UTF8')) AS value
  )
  SELECT value || '.' || data.base64url(ext.hmac(convert_to(value, 'UTF8'), convert_to(secret, 'UTF8'), 'sha256'))
  FROM unsigned
$$;

-- The signed-in user (claim `sub`); a write without it is refused.
CREATE FUNCTION data.current_username() RETURNS text LANGUAGE plpgsql STABLE AS $$
DECLARE
  username text := current_setting('request.jwt.claims', true)::jsonb ->> 'sub';
BEGIN
  IF username IS NULL THEN
    RAISE insufficient_privilege USING MESSAGE = 'Sign in first';
  END IF;
  RETURN username;
END $$;

------------------------------------------------------------------------------------------------------------------------
-- Sign in

CREATE FUNCTION api.login(username text, password text) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE
  account  data.users;
  settings data.settings;
BEGIN
  SELECT * INTO account FROM data.users u WHERE u.username = lower(btrim(login.username));
  -- One message for a wrong user and a wrong password; 28P01 -> HTTP 403 in PostgREST.
  IF account.id IS NULL OR ext.crypt(login.password, account.password_hash) <> account.password_hash THEN
    RAISE invalid_password USING MESSAGE = 'Invalid username or password';
  END IF;
  SELECT * INTO STRICT settings FROM data.settings;
  RETURN jsonb_build_object(
    'token', data.jwt_sign(
      jsonb_build_object(
        'role', account.role,
        'sub', account.username,
        'exp', extract(epoch FROM now() + settings.jwt_lifetime)::bigint
      ),
      settings.jwt_secret
    ),
    'username', account.username,
    'expires_at', now() + settings.jwt_lifetime
  );
END $$;
COMMENT ON FUNCTION api.login IS 'Signs in and returns a JWT for the Authorization: Bearer header.';

-- plpgsql, not sql: PostgreSQL inlines a simple SQL function into the query plan and checks EXECUTE only when it plans.
-- PostgREST keeps prepared statements per pooled connection, so a plan made for app_user would later serve web_anon
-- without the check (seen in tests: anonymous rpc/me answered 200). Every function in `api` is plpgsql for this reason.
CREATE FUNCTION api.me() RETURNS jsonb LANGUAGE plpgsql STABLE AS $$
BEGIN
  RETURN jsonb_build_object(
    'username', current_setting('request.jwt.claims', true)::jsonb ->> 'sub',
    'role', current_user
  );
END $$;
COMMENT ON FUNCTION api.me IS 'The signed-in user.';

------------------------------------------------------------------------------------------------------------------------
-- Master data: simple views are auto-updatable, so PostgREST gives POST/PATCH on them; column grants say what a client
-- may write. Rules (unique code, allowed unit, upper-case codes) are in the tables.

CREATE VIEW api.products AS
  SELECT id, sku, name, unit, created_at, updated_at FROM data.products;
COMMENT ON VIEW api.products IS 'Products. SKU is stored upper-case and must be unique.';

CREATE VIEW api.warehouses AS
  SELECT id, code, name, created_at, updated_at FROM data.warehouses;
COMMENT ON VIEW api.warehouses IS 'Warehouses. Code is stored upper-case and must be unique.';

CREATE VIEW api.stock AS
  SELECT s.product_id, p.sku, p.name AS product_name, p.unit,
         s.warehouse_id, w.code AS warehouse_code, w.name AS warehouse_name,
         s.quantity, s.updated_at
  FROM data.stock s
  JOIN data.products p ON p.id = s.product_id
  JOIN data.warehouses w ON w.id = s.warehouse_id;
COMMENT ON VIEW api.stock IS 'Stock per product and warehouse. Read-only: change it with rpc/receive and rpc/issue.';

CREATE VIEW api.movements AS
  SELECT m.id, m.kind, m.product_id, p.sku, p.name AS product_name, p.unit,
         m.warehouse_id, w.code AS warehouse_code, w.name AS warehouse_name,
         m.quantity, m.note, m.created_by, m.created_at
  FROM data.movements m
  JOIN data.products p ON p.id = m.product_id
  JOIN data.warehouses w ON w.id = m.warehouse_id;
COMMENT ON VIEW api.movements IS 'History of receipts and issues. Read-only.';

-- A join view is not updatable, and PostgreSQL says so (55000 -> HTTP 500) before it checks privileges. With INSTEAD OF
-- triggers the privilege check comes first (no grant -> 403); the trigger is the last line of defence.
CREATE FUNCTION data.read_only() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE SQLSTATE 'PT405' USING MESSAGE = format('%s is read-only: use rpc/receive and rpc/issue', TG_TABLE_NAME);
END $$;
CREATE TRIGGER stock_read_only INSTEAD OF INSERT OR UPDATE OR DELETE ON api.stock
  FOR EACH ROW EXECUTE FUNCTION data.read_only();
CREATE TRIGGER movements_read_only INSTEAD OF INSERT OR UPDATE OR DELETE ON api.movements
  FOR EACH ROW EXECUTE FUNCTION data.read_only();

------------------------------------------------------------------------------------------------------------------------
-- Stock operations: the only way to change stock. A movement and the stock change in one transaction.

CREATE FUNCTION data.check_movement(product_id uuid, warehouse_id uuid, quantity numeric) RETURNS void
LANGUAGE plpgsql STABLE SET search_path = '' AS $$
BEGIN
  IF quantity IS NULL OR quantity <= 0 THEN
    RAISE invalid_parameter_value USING MESSAGE = 'Quantity must be greater than zero', HINT = 'quantity';
  END IF;
  IF NOT EXISTS (SELECT FROM data.products p WHERE p.id = check_movement.product_id) THEN
    RAISE SQLSTATE 'PT404' USING MESSAGE = 'Unknown product', HINT = 'product_id';
  END IF;
  IF NOT EXISTS (SELECT FROM data.warehouses w WHERE w.id = check_movement.warehouse_id) THEN
    RAISE SQLSTATE 'PT404' USING MESSAGE = 'Unknown warehouse', HINT = 'warehouse_id';
  END IF;
END $$;

CREATE FUNCTION api.receive(product_id uuid, warehouse_id uuid, quantity numeric, note text DEFAULT NULL)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE
  movement_id uuid;
  new_quantity numeric;
BEGIN
  PERFORM data.check_movement(receive.product_id, receive.warehouse_id, receive.quantity);

  INSERT INTO data.movements (kind, product_id, warehouse_id, quantity, note, created_by)
  VALUES ('receipt', receive.product_id, receive.warehouse_id, receive.quantity, nullif(btrim(receive.note), ''),
          data.current_username())
  RETURNING id INTO movement_id;

  INSERT INTO data.stock AS s (product_id, warehouse_id, quantity)
  VALUES (receive.product_id, receive.warehouse_id, receive.quantity)
  ON CONFLICT ON CONSTRAINT stock_pkey
    DO UPDATE SET quantity = s.quantity + excluded.quantity, updated_at = now()
  RETURNING s.quantity INTO new_quantity;

  RETURN jsonb_build_object('movement_id', movement_id, 'quantity', new_quantity);
END $$;
COMMENT ON FUNCTION api.receive IS 'Books goods in: adds a receipt movement and raises the stock.';

CREATE FUNCTION api.issue(product_id uuid, warehouse_id uuid, quantity numeric, note text DEFAULT NULL)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE
  movement_id uuid;
  available numeric;
BEGIN
  PERFORM data.check_movement(issue.product_id, issue.warehouse_id, issue.quantity);

  -- The row lock serialises concurrent issues of the same product in the same warehouse.
  SELECT s.quantity INTO available
  FROM data.stock s
  WHERE s.product_id = issue.product_id AND s.warehouse_id = issue.warehouse_id
  FOR UPDATE;

  IF coalesce(available, 0) < issue.quantity THEN
    -- PTxyz -> HTTP xyz in PostgREST.
    RAISE SQLSTATE 'PT409' USING
      MESSAGE = format('Not enough stock: %s available', trim_scale(coalesce(available, 0))),
      HINT = 'quantity';
  END IF;

  INSERT INTO data.movements (kind, product_id, warehouse_id, quantity, note, created_by)
  VALUES ('issue', issue.product_id, issue.warehouse_id, issue.quantity, nullif(btrim(issue.note), ''),
          data.current_username())
  RETURNING id INTO movement_id;

  UPDATE data.stock s SET quantity = s.quantity - issue.quantity, updated_at = now()
  WHERE s.product_id = issue.product_id AND s.warehouse_id = issue.warehouse_id
  RETURNING s.quantity INTO available;

  RETURN jsonb_build_object('movement_id', movement_id, 'quantity', available);
END $$;
COMMENT ON FUNCTION api.issue IS 'Books goods out: adds an issue movement and lowers the stock; refused (409) when the stock is not enough.';

------------------------------------------------------------------------------------------------------------------------
-- Grants: web_anon may only sign in; app_user reads everything and writes master data and stock operations.

GRANT EXECUTE ON FUNCTION api.login(text, text) TO web_anon, app_user;
GRANT EXECUTE ON FUNCTION api.me() TO app_user;
GRANT EXECUTE ON FUNCTION api.receive(uuid, uuid, numeric, text), api.issue(uuid, uuid, numeric, text) TO app_user;

GRANT SELECT ON api.products, api.warehouses, api.stock, api.movements TO app_user;
GRANT INSERT (sku, name, unit), UPDATE (sku, name, unit) ON api.products TO app_user;
GRANT INSERT (code, name), UPDATE (code, name) ON api.warehouses TO app_user;
