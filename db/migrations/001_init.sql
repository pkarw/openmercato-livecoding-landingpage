create table if not exists tasks (
  id serial primary key,
  title text not null,
  status text not null default 'todo' check (status in ('todo', 'doing', 'done')),
  created_at timestamptz not null default now()
);

create index if not exists tasks_status_idx on tasks (status);
