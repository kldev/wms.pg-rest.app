-- Tables of the mini warehouse, in the private schema `data` (never exposed by PostgREST).
SET ROLE wms_owner;

CREATE TABLE data.settings (
  id           boolean PRIMARY KEY DEFAULT true CHECK (id),  -- one row
  jwt_secret   text NOT NULL CHECK (length(jwt_secret) >= 32),
  jwt_lifetime interval NOT NULL CHECK (jwt_lifetime > interval '0')
);

CREATE TABLE data.users (
  id            uuid PRIMARY KEY DEFAULT uuidv7(),
  username      text NOT NULL UNIQUE CHECK (username ~ '^[a-z0-9][a-z0-9._-]{1,31}$'),
  password_hash text NOT NULL,
  role          name NOT NULL DEFAULT 'app_user' CHECK (role IN ('app_user')),
  created_at    timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE data.products (
  id         uuid PRIMARY KEY DEFAULT uuidv7(),
  sku        text NOT NULL CHECK (sku ~ '^[A-Z0-9][A-Z0-9._-]{0,31}$'),
  name       text NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 200),
  unit       text NOT NULL DEFAULT 'pcs' CHECK (unit IN ('pcs', 'kg', 'm', 'l')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT products_sku_key UNIQUE (sku)
);

CREATE TABLE data.warehouses (
  id         uuid PRIMARY KEY DEFAULT uuidv7(),
  code       text NOT NULL CHECK (code ~ '^[A-Z0-9][A-Z0-9._-]{0,15}$'),
  name       text NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 200),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT warehouses_code_key UNIQUE (code)
);

-- Changed only by api.receive / api.issue, together with a movement.
CREATE TABLE data.stock (
  product_id   uuid NOT NULL REFERENCES data.products,
  warehouse_id uuid NOT NULL REFERENCES data.warehouses,
  quantity     numeric(14, 3) NOT NULL CHECK (quantity >= 0),
  updated_at   timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (product_id, warehouse_id)
);
CREATE INDEX stock_warehouse_idx ON data.stock (warehouse_id);

-- Append-only history.
CREATE TABLE data.movements (
  id           uuid PRIMARY KEY DEFAULT uuidv7(),
  kind         text NOT NULL CHECK (kind IN ('receipt', 'issue')),
  product_id   uuid NOT NULL REFERENCES data.products,
  warehouse_id uuid NOT NULL REFERENCES data.warehouses,
  quantity     numeric(14, 3) NOT NULL CHECK (quantity > 0),
  note         text CHECK (length(note) <= 500),
  created_by   text NOT NULL,
  created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX movements_product_idx ON data.movements (product_id, created_at DESC);
CREATE INDEX movements_warehouse_idx ON data.movements (warehouse_id, created_at DESC);
CREATE INDEX movements_created_idx ON data.movements (created_at DESC);

-- Codes are kept upper-case and trimmed whatever the client sends; updated_at follows every change.
CREATE FUNCTION data.normalize_code() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF TG_TABLE_NAME = 'products' THEN
    NEW.sku := upper(btrim(NEW.sku));
  ELSE
    NEW.code := upper(btrim(NEW.code));
  END IF;
  NEW.name := btrim(NEW.name);
  IF TG_OP = 'UPDATE' THEN
    NEW.updated_at := now();
  END IF;
  RETURN NEW;
END $$;

CREATE TRIGGER products_normalize BEFORE INSERT OR UPDATE ON data.products
  FOR EACH ROW EXECUTE FUNCTION data.normalize_code();
CREATE TRIGGER warehouses_normalize BEFORE INSERT OR UPDATE ON data.warehouses
  FOR EACH ROW EXECUTE FUNCTION data.normalize_code();
