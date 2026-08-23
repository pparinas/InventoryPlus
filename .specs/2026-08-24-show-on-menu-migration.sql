-- Run this in the Supabase SQL Editor. Adds a per-product toggle so an
-- owner can hide a product from their public QR menu without archiving it.
-- See Task: QR menu visibility control.

alter table public.products
  add column if not exists show_on_menu boolean not null default true;
