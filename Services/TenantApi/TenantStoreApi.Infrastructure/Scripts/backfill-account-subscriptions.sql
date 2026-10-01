-- ============================================================================================
-- Backfill account subscriptions for existing company owners (MySQL 8).
--
-- Only accounts that created their own workspace AND activated it (setup finished) get a plan.
-- An account that only joined other companies (e.g. an invited employee) gets nothing: it works
-- under each company owner's plan.
--
-- Step A marks existing companies activated from AuthApi's record of setup finishing
-- (TenantCreationRequests.HrDbReady). Step B gives each owner of an activated company -- with no
-- subscription yet -- the chosen plan as Active until @period_end. Same result as
-- SubscriptionBackfillService (TenantApi startup), which also only looks at activated companies.
--
-- Safe to re-run: activated companies stay as they are, and owners who already have a
-- subscription are skipped (one per account, enforced by a unique index).
--
-- Run AFTER the subscription migration (Tenants.ActivatedAtUtc, Plans columns, new tables).
-- The schema names below are the dev ones (tenantstore, auth, hrms) -- replace them if yours
-- differ. All times are UTC.
-- ============================================================================================

-- 1. Settings ---------------------------------------------------------------------------------
SET @plan_code  = 'premium';                -- plan to put existing accounts on
SET @period_end = '2027-01-01 00:00:00';    -- UTC: paid-up until (choose before go-live)
SET @cycle      = 'Monthly';                -- 'Monthly' or 'Annual'
SET @now        = UTC_TIMESTAMP(6);
SET @no_owner   = '00000000-0000-0000-0000-000000000000';

SELECT Id, MonthlyPrice, AnnualPrice
  INTO @plan_id, @monthly_price, @annual_price
  FROM tenantstore.Plans
 WHERE Code = @plan_code AND DeletedAt IS NULL
 LIMIT 1;

-- Stops here (NULL) if the plan code is wrong or the migration hasn't seeded the plans.
SELECT @plan_id AS plan_id, @monthly_price AS monthly_price, @annual_price AS annual_price;

START TRANSACTION;

-- A. Mark existing companies activated --------------------------------------------------------
-- From AuthApi: setup finished (HrDbReady). Uses the moment AuthApi recorded it.
UPDATE tenantstore.Tenants t
  JOIN auth.TenantCreationRequests r ON r.TenantId = t.Id
   SET t.ActivatedAtUtc = COALESCE(r.UpdatedAt, r.CreatedAt, @now)
 WHERE r.HrDbReady = 1
   AND t.ActivatedAtUtc IS NULL
   AND t.DeletedAt IS NULL;

SELECT ROW_COUNT() AS companies_activated;

-- Companies AuthApi has no creation record for (created before it tracked them). Review them:
-- they stay not-activated (and unrestricted) unless you activate them below.
SELECT t.Id, t.TenantName, t.UserId, t.CreatedAt
  FROM tenantstore.Tenants t
 WHERE t.DeletedAt IS NULL
   AND t.ActivatedAtUtc IS NULL
   AND NOT EXISTS (SELECT 1 FROM auth.TenantCreationRequests r WHERE r.TenantId = t.Id);

-- Optional: activate those that clearly finished setting up -- the HRIS has a Company record.
-- UPDATE tenantstore.Tenants t
--    SET t.ActivatedAtUtc = COALESCE(t.CreatedAt, @now)
--  WHERE t.DeletedAt IS NULL
--    AND t.ActivatedAtUtc IS NULL
--    AND NOT EXISTS (SELECT 1 FROM auth.TenantCreationRequests r WHERE r.TenantId = t.Id)
--    AND EXISTS (SELECT 1 FROM hrms.Companies c WHERE c.TenantId = t.Id AND c.DeletedAt IS NULL);

-- B. Preview: owners who will get a plan ------------------------------------------------------
SELECT t.UserId                        AS owner_user_id,
       COUNT(*)                        AS activated_companies,
       GROUP_CONCAT(t.TenantName SEPARATOR ', ') AS company_names,
       (SELECT m.InvitedEmail
          FROM tenantstore.Memberships m
         WHERE m.UserId = t.UserId AND m.IsHidden = 1 AND m.InvitedEmail IS NOT NULL
         LIMIT 1)                      AS owner_email
  FROM tenantstore.Tenants t
 WHERE t.DeletedAt IS NULL
   AND t.ActivatedAtUtc IS NOT NULL
   AND t.UserId <> @no_owner
   AND NOT EXISTS (SELECT 1 FROM tenantstore.AccountSubscriptions s WHERE s.OwnerUserId = t.UserId)
 GROUP BY t.UserId
 ORDER BY activated_companies DESC;

-- C. Backfill ---------------------------------------------------------------------------------
INSERT INTO tenantstore.AccountSubscriptions (
    Id, OwnerUserId, PlanId, SubStatus, BillingCycle,
    TrialEndsAtUtc, TrialExtended,
    CurrentPeriodStartUtc, CurrentPeriodEndUtc,
    GraceEndsAtUtc, CanceledAtUtc, PurgeAfterUtc, CancelAtPeriodEnd,
    PendingPlanId, PendingBillingCycle, PendingChangeTiming, PendingChangeEffectiveAtUtc,
    LockedMonthlyPrice, LockedAnnualPrice, ConcurrencyStamp,
    CreatedAt, UpdatedAt, DeletedAt, Status)
SELECT UUID(), owners.UserId, @plan_id, 'Active', @cycle,
       NULL, 0,
       @now, @period_end,
       NULL, NULL, NULL, 0,
       NULL, NULL, NULL, NULL,
       @monthly_price, @annual_price, UUID(),
       @now, NULL, NULL, 'Active'
  FROM (SELECT DISTINCT t.UserId
          FROM tenantstore.Tenants t
         WHERE t.DeletedAt IS NULL
           AND t.ActivatedAtUtc IS NOT NULL
           AND t.UserId <> @no_owner
           AND NOT EXISTS (SELECT 1 FROM tenantstore.AccountSubscriptions s WHERE s.OwnerUserId = t.UserId)
       ) AS owners
 WHERE @plan_id IS NOT NULL;

SELECT ROW_COUNT() AS subscriptions_created;

-- Billing history entry for each, as the app writes it (Type 'subscription.legacy-backfill').
INSERT INTO tenantstore.BillingEvents (
    Id, SubscriptionId, OwnerUserId, Type, PayloadJson, OccurredAtUtc, DedupeKey, Actor,
    CreatedAt, UpdatedAt, DeletedAt, Status)
SELECT UUID(), s.Id, s.OwnerUserId, 'subscription.legacy-backfill',
       JSON_OBJECT('plan', @plan_code, 'periodEnd', DATE_FORMAT(@period_end, '%Y-%m-%dT%H:%i:%sZ'), 'source', 'sql-script'),
       @now, NULL, 'system',
       @now, NULL, NULL, 'Active'
  FROM tenantstore.AccountSubscriptions s
 WHERE s.CreatedAt = @now
   AND s.PlanId = @plan_id;

-- D. Check, then COMMIT (or ROLLBACK if anything looks wrong) ---------------------------------
SELECT s.OwnerUserId, s.SubStatus, s.CurrentPeriodEndUtc, s.LockedMonthlyPrice
  FROM tenantstore.AccountSubscriptions s
 WHERE s.CreatedAt = @now;

COMMIT;
-- ROLLBACK;
