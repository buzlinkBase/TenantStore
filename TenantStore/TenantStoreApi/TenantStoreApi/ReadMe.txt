Phase 1: Tenant Registration
Actor: Tenant Service
Steps:
- Register Tenant → create tenant record with state = Pending.
- AuthService → Check Email → validate if admin email exists or create new user.
- Store Outbox → tenant.pending.
- Publish Event → tenant.created.

Phase 2: Admin User Creation
Actor: User Service
Triggered by: tenant.created
Steps:
- Register Admin User → create user linked to tenant.
- On Success → publish admin.user.created.
- On Failure → publish admin.user.failed (so TenantService can rollback or mark tenant as Error).

Phase 3: Tenant Confirmation Email
Actor: Tenant Service
Triggered by: admin.user.created
Steps:
- Update Tenant State → ForConfirmation.
- Create Outbox → tenant.for.confirmation.
- Send Confirmation Email → Notification Service (notif.tenant.confirmation).
- Optional Reminder Flow → schedule reminders if not confirmed within X days.

Phase 4: User Email Confirmation
Actor: Auth Service
Endpoint: /email/confirm/token
Steps:
- Validate Token → confirm user email.
- Update User State → Confirmed.
- Create Outbox → tenant.for.activation.
- Publish Event → admin.user.confirmed.
- Redirect User → to sign‑in/login page (frontend handles navigation).

Phase 5: Tenant Activation
Actor: Tenant Service
Triggered by: admin.user.confirmed
Steps:
- Update Tenant State → Active.
- Create Outbox → tenant.active.
- Publish Event → tenant.activated.

Phase 6: User Activation
Actor: Auth Service
Triggered by: tenant.activated
Steps:
- Update User State → Active.
- Remove Outbox → tenant.for.activation.
- Publish Event → user.active.

Phase 7: Cleanup
Actor: Tenant Service
Triggered by: user.active
Steps:
- Remove Outbox → admin.user.confirmed.
- Publish Event → tenant.cleanup.success.
Actor: Auth Service
Triggered by: tenant.cleanup.success
Steps:
- Remove Outbox → finalize saga.

- Failure Handling: Added admin.user.failed so TenantService can rollback or mark tenant as Error.
- Reminder Flow: Tenants stuck in ForConfirmation can trigger reminder emails or expire after X days.
- Clear State Machine: Tenant states = Pending → ForConfirmation → Active → Expired/Error. User states = Registered → Confirmed → Active.
- Audit Safety: Outbox entries are marked processed/removed after success, ensuring no duplicates.
- Frontend Redirect: Explicitly handled at /email/confirm/token → redirect to login page.


paths
Tenant Registration
Teanant Verification

