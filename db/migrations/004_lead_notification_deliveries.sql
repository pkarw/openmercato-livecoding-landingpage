create table if not exists lead_notification_deliveries (
  id bigserial primary key,
  lead_id integer not null references leads(id) on delete cascade,
  kind text not null check (kind in ('lead', 'inbox')),
  lead_email text not null,
  lead_name text,
  interest text not null check (interest in ('openmercato', 'aitechleaders', 'both')),
  discount_code text not null,
  discount_percent integer not null check (discount_percent > 0 and discount_percent < 100),
  lead_created_at timestamptz not null,
  leads_inbox text not null,
  status text not null default 'pending' check (status in ('pending', 'delivered', 'failed')),
  attempt_count integer not null default 0 check (attempt_count >= 0),
  next_attempt_at timestamptz not null default now(),
  locked_until timestamptz,
  lease_token uuid,
  delivered_at timestamptz,
  last_error text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (lead_id, kind)
);

create index if not exists lead_notification_deliveries_pending_idx
  on lead_notification_deliveries (next_attempt_at, id)
  where status = 'pending';

create index if not exists lead_notification_deliveries_failed_idx
  on lead_notification_deliveries (updated_at desc)
  where status = 'failed';
