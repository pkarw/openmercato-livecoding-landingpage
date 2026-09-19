-- Discount codes are redeemed without an email-scoped lookup, so one code must identify one lead.
-- Never rewrite an issued reservation automatically: abort before changing the schema when legacy
-- duplicates exist, so an operator can reconcile them with the affected customers first.
do $$
begin
  if exists (
    select 1
      from leads
     group by discount_code
    having count(*) > 1
  ) then
    raise exception using
      message = 'Cannot enforce unique discount codes: duplicate issued codes exist.',
      hint = 'Find duplicates with: select discount_code, array_agg(email order by email) from leads group by discount_code having count(*) > 1; Reconcile issued reservations, then rerun migrations.';
  end if;
end
$$;

alter table leads
  add constraint leads_discount_code_key unique (discount_code);
