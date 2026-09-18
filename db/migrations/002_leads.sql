create table if not exists leads (
  id serial primary key,
  email text not null unique,
  name text,
  interest text not null check (interest in ('openmercato', 'aitechleaders', 'both')),
  discount_code text not null,
  privacy_accepted boolean not null default false,
  marketing_consent boolean not null default false,
  source text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create index if not exists leads_interest_idx on leads (interest);
create index if not exists leads_created_at_idx on leads (created_at desc);
