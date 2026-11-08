# Design

## Context

See [proposal.md](proposal.md) for the motivation.

Local orchestration runs Keycloak only outside publish mode.
The `keycloak-init` project authenticates as the Keycloak administrator and calls `KeycloakRealmReconciler.ReconcileAsync`.
That method creates missing realm state, imports missing development accounts by stable ID through `partialImport`, and never removes accounts.
It refuses non-loopback Keycloak endpoints.
The AppHost already passes one generated, persisted secret parameter to it: `keycloak-provisioning-secret`.

The realm file defines the three development accounts with client roles, `UPDATE_PASSWORD`, and no credentials.
The integration test fixture runs the same reconciler, then calls its own `SetPasswordAsync` helper for those accounts.
That helper resets the password with `temporary = false` and clears all required actions.

The identity spec requires that committed configuration contains no passwords or client secrets.
It also requires password grants to stay disabled on application clients.

## Goals / Non-Goals

**Goals:**

- Make the development accounts usable after `aspire start` with no manual Keycloak console steps.
- Reuse the existing parameter and reconciler patterns, so local orchestration and integration tests share one code path.
- Repair existing local Keycloak volumes on the next start without a volume reset.

**Non-Goals:**

- Separate passwords for each development account.
- Keeping existing passwords in sync with later secret changes.
- Changing the sign-in flow, client settings, or the provisioning process for real accounts.
- Any production or published-environment credential handling.

## Decisions

### 1. One shared password from an Aspire secret parameter

Add `keycloak-dev-password` in the AppHost's local Keycloak block, next to `keycloak-provisioning-secret`.
Use `GenerateParameterDefault` with alphanumeric characters only and a length of 16, plus `secret: true` and `persist: true`.
Aspire then stores the generated value in AppHost user secrets as `Parameters:keycloak-dev-password`.
Developers read it with `dotnet user-secrets list --project tools/AppHost` and replace it with `dotnet user-secrets set`.
Alphanumeric characters keep the value easy to type on Keycloak's sign-in page.

Alternatives considered:

- **Committed default value, such as `Dev123!`.** This is the simplest option, but it breaks the spec rule against committed passwords and makes the password known to anyone who can read the repository.
- **Credentials in the realm file.** This has the same problem, and the startup import applies only to fresh volumes.
- **One parameter per account.** This adds configuration without adding protection, because all three accounts are local and use seeded data.

### 2. Apply passwords in the shared reconciler

Add a `developmentPassword` argument to `KeycloakRealmReconciler.ReconcileAsync`.
`keycloak-init` reads it from a required `KEYCLOAK_DEV_PASSWORD` environment variable, the same way it reads `KEYCLOAK_PROVISIONING_SECRET`.
The reconciler rejects a value shorter than 8 characters, so a misconfigured secret fails startup instead of creating weak or empty credentials.

Integration tests pass their existing generated password to the reconciler and stop calling `SetPasswordAsync` for the three development accounts.
The tests then cover the code path that local orchestration uses.

The alternative is a separate initializer step or project.
That would duplicate the administrator client, the loopback check, and the test wiring for a few API calls.

### 3. Set a password only when the account has none

After it imports missing users, the reconciler handles each account listed in the realm file's `users` array, by its stable ID:

1. Read `GET /admin/realms/medicalapp/users/{id}/credentials`.
2. If any credential has type `password`, leave the account unchanged.
3. Otherwise, call `PUT .../users/{id}/reset-password` with `temporary = false`.
4. Then remove `UPDATE_PASSWORD` from the account's required actions and keep any other required actions.

This keeps passwords that developers set and fixes existing volumes where the accounts have no password.
It runs on every start and is idempotent.
The password check costs one read per development account.

Alternatives considered:

- **Always reset to the secret.** This makes the secret the single source of truth, but it silently overwrites passwords that developers chose in Keycloak.
- **Clear all required actions, as the test helper does.** This could hide an action that a developer added on purpose.

### 4. Scope by the realm file's account list

Only accounts listed in the realm file's `users` array receive the password.
Accounts provisioned through `/api/accounts` have other IDs and are never inspected.
They keep the documented temporary-password flow.

### 5. Remove `UPDATE_PASSWORD` from the realm file

Remove `UPDATE_PASSWORD` from the realm file so that a fresh import does not add the action.
Decision 3 still handles volumes that already have it.

## Risks / Trade-offs

- [A secret change does not reach accounts that already have passwords] → Document this in the Keycloak README, along with the fix: delete the account's password credential in the Keycloak console and restart, or reset the password there.
- [A developer chooses a weak password] → The minimum length blocks empty or very short values. The accounts reach only local seeded data, and the initializer works only with loopback endpoints.
- [The secret appears in `keycloak-init`'s environment in the Aspire dashboard] → The dashboard masks secret parameter values, as it already does for the provisioning secret.
- [The test fixture no longer clears every required action for development accounts] → The realm file defines no other required actions for them, so test sign-in behavior does not change.

## Migration Plan

No data migration is needed.
On the next `aspire start`, `keycloak-init` adds passwords to existing development accounts that have none and removes their `UPDATE_PASSWORD` action.
Accounts that already have a password are unchanged.
Rollback means reverting the change. Accounts keep any passwords set while it was active.
