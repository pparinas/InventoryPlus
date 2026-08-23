-- Run this in the Supabase SQL Editor once the login-as-user Edge Function
-- (supabase/functions/login-as-user) has been deployed. See Task: admin
-- "login as user" impersonation.

create table if not exists public.admin_impersonation_log (
  id uuid primary key default gen_random_uuid(),
  admin_id uuid not null references auth.users(id),
  target_user_id uuid not null references auth.users(id),
  created_at timestamptz not null default now()
);

alter table public.admin_impersonation_log enable row level security;

-- Rows are only ever written by the Edge Function using the service-role
-- key (which bypasses RLS entirely), so no insert policy is needed here --
-- only a read policy so admins can review the log from within the app later.
create policy "Admins can view impersonation log"
  on public.admin_impersonation_log for select
  using (
    exists (
      select 1 from public.user_profiles p
      where p.guid = auth.uid() and p.is_admin = true
    )
  );
