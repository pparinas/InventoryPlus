-- =============================================================================
-- Negosyo360 (InventoryPlus) — Recreate All Tables
-- Run this in the Supabase SQL Editor (Dashboard > SQL Editor > New Query)
-- =============================================================================

-- ─── 1. user_profiles ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.user_profiles (
    guid       uuid PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    email      text NOT NULL DEFAULT '',
    username   text,
    is_admin   boolean NOT NULL DEFAULT false,
    is_active  boolean NOT NULL DEFAULT true,
    trial_expires_at    timestamptz,          -- 7-day free trial (set by trigger)
    subscription_expiry timestamptz,          -- paid subscription (set by admin)
    created_at timestamptz NOT NULL DEFAULT now()
);

-- ─── 2. account_settings ────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.account_settings (
    owner_guid            uuid PRIMARY KEY REFERENCES public.user_profiles(guid) ON DELETE CASCADE,
    company_name          text NOT NULL DEFAULT 'Negosyo360',
    logo_url              text NOT NULL DEFAULT '',
    use_logo_for_branding boolean NOT NULL DEFAULT false,
    pin_hash              text NOT NULL DEFAULT '',
    show_inventory_tab    boolean NOT NULL DEFAULT true,
    show_opex_tab         boolean NOT NULL DEFAULT false,
    color_scheme          text NOT NULL DEFAULT 'indigo'
);

-- ─── 3. ingredients ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.ingredients (
    guid        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_guid  uuid NOT NULL REFERENCES public.user_profiles(guid) ON DELETE CASCADE,
    name        text NOT NULL DEFAULT '',
    unit        text NOT NULL DEFAULT '',
    stock       double precision NOT NULL DEFAULT 0,
    cost_per_unit double precision NOT NULL DEFAULT 0,
    type        text NOT NULL DEFAULT 'Ingredient',   -- 'Ingredient' or 'Necessity'
    is_archived boolean NOT NULL DEFAULT false,
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- ─── 4. products ────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.products (
    guid            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_guid      uuid NOT NULL REFERENCES public.user_profiles(guid) ON DELETE CASCADE,
    name            text NOT NULL DEFAULT '',
    selling_price   double precision NOT NULL DEFAULT 0,
    tax_rate        double precision NOT NULL DEFAULT 0,
    image_url       text NOT NULL DEFAULT '',
    has_ingredients boolean NOT NULL DEFAULT true,
    stock_count     double precision NOT NULL DEFAULT 0,
    is_archived     boolean NOT NULL DEFAULT false
);

-- ─── 5. product_ingredients (junction table) ────────────────────────────────
CREATE TABLE IF NOT EXISTS public.product_ingredients (
    guid              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_guid        uuid NOT NULL REFERENCES public.user_profiles(guid) ON DELETE CASCADE,
    product_id        uuid NOT NULL REFERENCES public.products(guid)     ON DELETE CASCADE,
    ingredient_id     uuid NOT NULL REFERENCES public.ingredients(guid)  ON DELETE CASCADE,
    quantity_required double precision NOT NULL DEFAULT 0
);

-- ─── 6. sales ───────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.sales (
    guid            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_guid      uuid NOT NULL REFERENCES public.user_profiles(guid) ON DELETE CASCADE,
    product_id      uuid NOT NULL REFERENCES public.products(guid)      ON DELETE CASCADE,
    product_name    text NOT NULL DEFAULT '',
    quantity_sold   integer NOT NULL DEFAULT 0,
    total_amount    double precision NOT NULL DEFAULT 0,
    tax_amount      double precision NOT NULL DEFAULT 0,
    profit_amount   double precision NOT NULL DEFAULT 0,
    date            timestamptz NOT NULL DEFAULT now(),
    note            text NOT NULL DEFAULT '',
    payment_method  text NOT NULL DEFAULT 'Cash',
    is_voided       boolean NOT NULL DEFAULT false,
    customer_name   text NOT NULL DEFAULT '',
    discount_amount double precision NOT NULL DEFAULT 0,
    discount_type   text NOT NULL DEFAULT 'None'       -- 'None', 'Percentage', 'Fixed'
);

-- ─── 7. opex (operating expenses) ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.opex (
    guid        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_guid  uuid NOT NULL REFERENCES public.user_profiles(guid) ON DELETE CASCADE,
    name        text NOT NULL DEFAULT '',
    category    text NOT NULL DEFAULT 'Other',
    amount      double precision NOT NULL DEFAULT 0,
    date        timestamptz NOT NULL DEFAULT now(),
    note        text NOT NULL DEFAULT '',
    is_recurring boolean NOT NULL DEFAULT false,
    recurrence  text NOT NULL DEFAULT 'None',          -- 'None','Daily','Weekly','Monthly','Yearly'
    is_archived boolean NOT NULL DEFAULT false
);

-- ─── 8. invite_tokens ──────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.invite_tokens (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    token         text NOT NULL DEFAULT '',
    created_at    timestamptz NOT NULL DEFAULT now(),
    expires_at    timestamptz NOT NULL,
    is_used       boolean NOT NULL DEFAULT false,
    created_by    uuid NOT NULL REFERENCES public.user_profiles(guid) ON DELETE CASCADE,
    used_by_email text NOT NULL DEFAULT ''
);


-- =============================================================================
-- INDEXES  (speed up the most common queries)
-- =============================================================================
CREATE INDEX IF NOT EXISTS idx_ingredients_owner   ON public.ingredients(owner_guid);
CREATE INDEX IF NOT EXISTS idx_products_owner      ON public.products(owner_guid);
CREATE INDEX IF NOT EXISTS idx_product_ingredients_product ON public.product_ingredients(product_id);
CREATE INDEX IF NOT EXISTS idx_product_ingredients_ingredient ON public.product_ingredients(ingredient_id);
CREATE INDEX IF NOT EXISTS idx_sales_owner         ON public.sales(owner_guid);
CREATE INDEX IF NOT EXISTS idx_sales_date          ON public.sales(date);
CREATE INDEX IF NOT EXISTS idx_opex_owner          ON public.opex(owner_guid);
CREATE INDEX IF NOT EXISTS idx_invite_tokens_token ON public.invite_tokens(token);


-- =============================================================================
-- ROW LEVEL SECURITY  (each user sees only their own data)
-- =============================================================================

-- Helper: returns the current authenticated user's UUID
-- Supabase sets auth.uid() automatically from the JWT.

-- ── Helper function: check if current user is admin (bypasses RLS) ──────────
CREATE OR REPLACE FUNCTION public.is_admin()
RETURNS boolean
LANGUAGE sql
SECURITY DEFINER SET search_path = ''
STABLE
AS $$
    SELECT COALESCE(
        (SELECT is_admin FROM public.user_profiles WHERE guid = auth.uid()),
        false
    );
$$;

-- ── user_profiles ───────────────────────────────────────────────────────────
ALTER TABLE public.user_profiles ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users can view own profile" ON public.user_profiles;
CREATE POLICY "Users can view own profile"
    ON public.user_profiles FOR SELECT
    USING (guid = auth.uid());

DROP POLICY IF EXISTS "Users can update own profile" ON public.user_profiles;
CREATE POLICY "Users can update own profile"
    ON public.user_profiles FOR UPDATE
    USING (guid = auth.uid());

DROP POLICY IF EXISTS "Users can insert own profile" ON public.user_profiles;
CREATE POLICY "Users can insert own profile"
    ON public.user_profiles FOR INSERT
    WITH CHECK (guid = auth.uid());

-- Admin can view all profiles (for user management page)
DROP POLICY IF EXISTS "Admins can view all profiles" ON public.user_profiles;
CREATE POLICY "Admins can view all profiles"
    ON public.user_profiles FOR SELECT
    USING (public.is_admin());

-- Admin can update all profiles
DROP POLICY IF EXISTS "Admins can update all profiles" ON public.user_profiles;
CREATE POLICY "Admins can update all profiles"
    ON public.user_profiles FOR UPDATE
    USING (public.is_admin());

-- ── account_settings ────────────────────────────────────────────────────────
ALTER TABLE public.account_settings ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users can manage own settings" ON public.account_settings;
CREATE POLICY "Users can manage own settings"
    ON public.account_settings FOR ALL
    USING (owner_guid = auth.uid());

-- ── ingredients ─────────────────────────────────────────────────────────────
ALTER TABLE public.ingredients ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users can manage own ingredients" ON public.ingredients;
CREATE POLICY "Users can manage own ingredients"
    ON public.ingredients FOR ALL
    USING (owner_guid = auth.uid());

-- ── products ────────────────────────────────────────────────────────────────
ALTER TABLE public.products ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users can manage own products" ON public.products;
CREATE POLICY "Users can manage own products"
    ON public.products FOR ALL
    USING (owner_guid = auth.uid());

-- ── product_ingredients ─────────────────────────────────────────────────────
ALTER TABLE public.product_ingredients ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users can manage own product_ingredients" ON public.product_ingredients;
CREATE POLICY "Users can manage own product_ingredients"
    ON public.product_ingredients FOR ALL
    USING (owner_guid = auth.uid());

-- ── sales ───────────────────────────────────────────────────────────────────
ALTER TABLE public.sales ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users can manage own sales" ON public.sales;
CREATE POLICY "Users can manage own sales"
    ON public.sales FOR ALL
    USING (owner_guid = auth.uid());

-- ── opex ────────────────────────────────────────────────────────────────────
ALTER TABLE public.opex ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users can manage own opex" ON public.opex;
CREATE POLICY "Users can manage own opex"
    ON public.opex FOR ALL
    USING (owner_guid = auth.uid());

-- ── invite_tokens ───────────────────────────────────────────────────────────
ALTER TABLE public.invite_tokens ENABLE ROW LEVEL SECURITY;

-- Admins can manage all invite tokens
DROP POLICY IF EXISTS "Admins can manage invite tokens" ON public.invite_tokens;
CREATE POLICY "Admins can manage invite tokens"
    ON public.invite_tokens FOR ALL
    USING (public.is_admin());

-- Anyone with a valid token can read it (for registration validation)
DROP POLICY IF EXISTS "Anyone can validate tokens" ON public.invite_tokens;
CREATE POLICY "Anyone can validate tokens"
    ON public.invite_tokens FOR SELECT
    USING (true);


-- =============================================================================
-- DONE — Now run supabase_functions.sql for triggers and functions.
-- Verify tables: SELECT tablename FROM pg_tables WHERE schemaname = 'public';
-- =============================================================================
