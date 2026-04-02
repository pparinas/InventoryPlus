-- =============================================================================
-- Negosyo360 — Database Functions & Triggers
-- Run this in the Supabase SQL Editor AFTER supabase_recreate_tables.sql
-- =============================================================================


-- =============================================================================
-- FUNCTION 1: handle_new_user — auto-create profile + settings on signup
-- =============================================================================
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER SET search_path = ''
AS $$
BEGIN
    -- Create user profile with 7-day free trial
    INSERT INTO public.user_profiles (guid, email, username, trial_expires_at, created_at)
    VALUES (
        NEW.id,
        NEW.email,
        COALESCE(NEW.raw_user_meta_data->>'username', split_part(NEW.email, '@', 1)),
        now() + interval '7 days',
        now()
    );

    -- Create default account settings
    INSERT INTO public.account_settings (owner_guid)
    VALUES (NEW.id);

    RETURN NEW;
END;
$$;

-- Drop existing trigger if it exists, then recreate
DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;
CREATE TRIGGER on_auth_user_created
    AFTER INSERT ON auth.users
    FOR EACH ROW EXECUTE FUNCTION public.handle_new_user();


-- =============================================================================
-- FUNCTION 2: get_email_by_username — resolve username to email for login
-- Callable by anonymous users (before auth) so login can accept username
-- =============================================================================
CREATE OR REPLACE FUNCTION public.get_email_by_username(username_input text)
RETURNS text
LANGUAGE plpgsql
SECURITY DEFINER SET search_path = ''
AS $$
DECLARE
    found_email text;
BEGIN
    SELECT email INTO found_email
    FROM public.user_profiles
    WHERE lower(username) = lower(username_input)
      AND is_active = true
    LIMIT 1;

    RETURN found_email;  -- returns NULL if not found
END;
$$;

-- Grant anonymous access so unauthenticated users can call it during login
GRANT EXECUTE ON FUNCTION public.get_email_by_username(text) TO anon;
GRANT EXECUTE ON FUNCTION public.get_email_by_username(text) TO authenticated;


-- =============================================================================
-- FUNCTION 3: backfill_existing_auth_users — one-time helper
-- Run SELECT public.backfill_existing_auth_users(); after creating the tables
-- to create profiles for auth users that already exist but have no profile row.
-- =============================================================================
CREATE OR REPLACE FUNCTION public.backfill_existing_auth_users()
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER SET search_path = ''
AS $$
BEGIN
    INSERT INTO public.user_profiles (guid, email, username, trial_expires_at, created_at)
    SELECT
        u.id,
        u.email,
        COALESCE(u.raw_user_meta_data->>'username', split_part(u.email, '@', 1)),
        now() + interval '7 days',
        COALESCE(u.created_at, now())
    FROM auth.users u
    WHERE NOT EXISTS (
        SELECT 1 FROM public.user_profiles p WHERE p.guid = u.id
    );

    INSERT INTO public.account_settings (owner_guid)
    SELECT p.guid
    FROM public.user_profiles p
    WHERE NOT EXISTS (
        SELECT 1 FROM public.account_settings s WHERE s.owner_guid = p.guid
    );
END;
$$;


-- =============================================================================
-- POST-SETUP: Run these after executing both SQL files
-- =============================================================================
-- SELECT public.backfill_existing_auth_users();
-- UPDATE public.user_profiles SET is_admin = true WHERE email = 'your-email@example.com';
