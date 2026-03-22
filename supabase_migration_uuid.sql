-- ============================================================
-- InventoryPlus – Migration: enforce UUID on all id columns
-- Run this in: Supabase Dashboard → SQL Editor
--
-- WHY: If the tables were created via the Supabase UI before the
-- main schema script was run, the UI defaults every "id" column
-- to int8 (bigint). Because the schema uses CREATE TABLE IF NOT
-- EXISTS, the UUID version was silently skipped.
--
-- SAFE WHEN: All app tables are empty (no real data yet).
-- The script drops every app table and recreates it with the
-- correct UUID primary keys and foreign key constraints.
-- ============================================================

-- ------------------------------------------------------------
-- 0. Drop triggers first (must happen before tables are dropped)
-- ------------------------------------------------------------
DROP TRIGGER IF EXISTS on_auth_user_created             ON auth.users;
DROP TRIGGER IF EXISTS trg_ingredients_updated_at       ON public.ingredients;
DROP TRIGGER IF EXISTS trg_products_updated_at          ON public.products;
DROP TRIGGER IF EXISTS trg_account_settings_updated_at  ON public.account_settings;

-- Drop functions
DROP FUNCTION IF EXISTS public.handle_new_user();
DROP FUNCTION IF EXISTS public.set_updated_at();

-- Drop old storage policies
DROP POLICY IF EXISTS "owner_product_images" ON storage.objects;
DROP POLICY IF EXISTS "owner_branding"        ON storage.objects;

-- Drop app tables in reverse-dependency order
DROP TABLE IF EXISTS public.account_settings    CASCADE;
DROP TABLE IF EXISTS public.sales               CASCADE;
DROP TABLE IF EXISTS public.product_ingredients CASCADE;
DROP TABLE IF EXISTS public.products            CASCADE;
DROP TABLE IF EXISTS public.ingredients         CASCADE;
DROP TABLE IF EXISTS public.user_profiles       CASCADE;

-- ------------------------------------------------------------
-- 1. INGREDIENTS
-- ------------------------------------------------------------
CREATE TABLE public.ingredients (
    guid            UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_guid      UUID        NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    name            TEXT        NOT NULL,
    unit            TEXT        NOT NULL DEFAULT 'pcs',
    stock           FLOAT8      NOT NULL DEFAULT 0,
    cost_per_unit   FLOAT8      NOT NULL DEFAULT 0,
    type            TEXT        NOT NULL DEFAULT 'Ingredient'
                    CHECK (type IN ('Ingredient', 'Necessity')),
    is_archived     BOOLEAN     NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ------------------------------------------------------------
-- 2. PRODUCTS
-- ------------------------------------------------------------
CREATE TABLE public.products (
    guid            UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_guid      UUID        NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    name            TEXT        NOT NULL,
    selling_price   FLOAT8      NOT NULL DEFAULT 0,
    tax_rate        FLOAT8      NOT NULL DEFAULT 0,
    image_url       TEXT        NOT NULL DEFAULT '',
    is_archived     BOOLEAN     NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ------------------------------------------------------------
-- 3. PRODUCT_INGREDIENTS
-- ------------------------------------------------------------
CREATE TABLE public.product_ingredients (
    guid                UUID    PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_guid          UUID    NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    product_id          UUID    NOT NULL REFERENCES public.products(guid) ON DELETE CASCADE,
    ingredient_id       UUID    NOT NULL REFERENCES public.ingredients(guid) ON DELETE CASCADE,
    quantity_required   FLOAT8  NOT NULL DEFAULT 0,
    UNIQUE (product_id, ingredient_id)
);

-- ------------------------------------------------------------
-- 4. SALES
-- ------------------------------------------------------------
CREATE TABLE public.sales (
    guid            UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_guid      UUID        NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    product_id      UUID        NOT NULL REFERENCES public.products(guid) ON DELETE RESTRICT,
    product_name    TEXT        NOT NULL,
    quantity_sold   INT         NOT NULL DEFAULT 1,
    total_amount    FLOAT8      NOT NULL DEFAULT 0,
    tax_amount      FLOAT8      NOT NULL DEFAULT 0,
    profit_amount   FLOAT8      NOT NULL DEFAULT 0,
    payment_method  TEXT        NOT NULL DEFAULT 'Cash'
                    CHECK (payment_method IN ('Cash', 'GCash')),
    note            TEXT        NOT NULL DEFAULT '',
    date            TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ------------------------------------------------------------
-- 5. USER PROFILES
-- ------------------------------------------------------------
CREATE TABLE public.user_profiles (
    guid                    UUID        PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    email                   TEXT        NOT NULL,
    is_admin                BOOLEAN     NOT NULL DEFAULT FALSE,
    is_active               BOOLEAN     NOT NULL DEFAULT TRUE,
    subscription_expires_at TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ------------------------------------------------------------
-- 6. ACCOUNT SETTINGS
-- ------------------------------------------------------------
CREATE TABLE public.account_settings (
    owner_guid              UUID        PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    company_name            TEXT        NOT NULL DEFAULT 'InventoryPlus',
    logo_url                TEXT        NOT NULL DEFAULT '',
    use_logo_for_branding   BOOLEAN     NOT NULL DEFAULT FALSE,
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ------------------------------------------------------------
-- 7. AUTO-CREATE USER PROFILE ON SIGNUP
-- ------------------------------------------------------------
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER SET search_path = public
AS $$
BEGIN
    INSERT INTO public.user_profiles (guid, email, is_admin, is_active)
    VALUES (NEW.id, NEW.email, FALSE, TRUE)
    ON CONFLICT (id) DO NOTHING;
    RETURN NEW;
END;
$$;

CREATE TRIGGER on_auth_user_created
    AFTER INSERT ON auth.users
    FOR EACH ROW EXECUTE FUNCTION public.handle_new_user();

-- ------------------------------------------------------------
-- 8. UPDATED_AT TRIGGERS
-- ------------------------------------------------------------
CREATE OR REPLACE FUNCTION public.set_updated_at()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_ingredients_updated_at
    BEFORE UPDATE ON public.ingredients
    FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

CREATE TRIGGER trg_products_updated_at
    BEFORE UPDATE ON public.products
    FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

CREATE TRIGGER trg_account_settings_updated_at
    BEFORE UPDATE ON public.account_settings
    FOR EACH ROW EXECUTE FUNCTION public.set_updated_at();

-- ------------------------------------------------------------
-- 9. ROW LEVEL SECURITY
-- ------------------------------------------------------------
ALTER TABLE public.ingredients         ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.products            ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.product_ingredients ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.sales               ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.user_profiles       ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.account_settings    ENABLE ROW LEVEL SECURITY;

CREATE POLICY "owner_all_ingredients"         ON public.ingredients
    FOR ALL USING (auth.uid() = owner_guid) WITH CHECK (auth.uid() = owner_guid);

CREATE POLICY "owner_all_products"            ON public.products
    FOR ALL USING (auth.uid() = owner_guid) WITH CHECK (auth.uid() = owner_guid);

CREATE POLICY "owner_all_product_ingredients" ON public.product_ingredients
    FOR ALL USING (auth.uid() = owner_guid) WITH CHECK (auth.uid() = owner_guid);

CREATE POLICY "owner_all_sales"               ON public.sales
    FOR ALL USING (auth.uid() = owner_guid) WITH CHECK (auth.uid() = owner_guid);

CREATE POLICY "owner_own_profile"             ON public.user_profiles
    FOR ALL USING (auth.uid() = guid) WITH CHECK (auth.uid() = guid);

CREATE POLICY "owner_all_account_settings"    ON public.account_settings
    FOR ALL USING (auth.uid() = owner_guid) WITH CHECK (auth.uid() = owner_guid);

-- ------------------------------------------------------------
-- 10. STORAGE BUCKETS
-- ------------------------------------------------------------
INSERT INTO storage.buckets (id, name, public)
VALUES ('product-images', 'product-images', FALSE)
ON CONFLICT (id) DO NOTHING;

INSERT INTO storage.buckets (id, name, public)
VALUES ('branding', 'branding', FALSE)
ON CONFLICT (id) DO NOTHING;

CREATE POLICY "owner_product_images" ON storage.objects
    FOR ALL USING (
        bucket_id = 'product-images'
        AND auth.uid()::text = (storage.foldername(name))[1]
    )
    WITH CHECK (
        bucket_id = 'product-images'
        AND auth.uid()::text = (storage.foldername(name))[1]
    );

CREATE POLICY "owner_branding" ON storage.objects
    FOR ALL USING (
        bucket_id = 'branding'
        AND auth.uid()::text = (storage.foldername(name))[1]
    )
    WITH CHECK (
        bucket_id = 'branding'
        AND auth.uid()::text = (storage.foldername(name))[1]
    );

-- ------------------------------------------------------------
-- 11. INDEXES
-- ------------------------------------------------------------
CREATE INDEX idx_ingredients_owner        ON public.ingredients (owner_guid);
CREATE INDEX idx_ingredients_archived     ON public.ingredients (owner_guid, is_archived);
CREATE INDEX idx_products_owner           ON public.products (owner_guid);
CREATE INDEX idx_products_archived        ON public.products (owner_guid, is_archived);
CREATE INDEX idx_product_ingredients_prod ON public.product_ingredients (product_id);
CREATE INDEX idx_product_ingredients_ing  ON public.product_ingredients (ingredient_id);
CREATE INDEX idx_sales_owner              ON public.sales (owner_guid);
CREATE INDEX idx_sales_date               ON public.sales (owner_guid, date DESC);
CREATE INDEX idx_account_settings_owner   ON public.account_settings (owner_guid);
