# Proposal

## Why

Local Keycloak imports the `development-doctor`, `development-patient`, and `development-administrator` accounts without passwords, and each account requires a password update.
A developer must open the Keycloak administrator console and set three passwords before signing in through Swagger, and must repeat this after every Keycloak volume reset.
Integration tests already work around the gap with their own password helper, so local orchestration is the only path without working development credentials.

## What Changes

- Add an Aspire secret parameter for the shared development account password. Aspire generates it once and persists it, and a developer can override it through AppHost user secrets.
- Pass the parameter to the local Keycloak initializer.
- During reconciliation, the initializer gives each development account from the realm file that password when the account has no password credential, and clears the account's required actions.
- Leave existing passwords unchanged, so a password that a developer set stays in effect after a restart.
- Remove the `UPDATE_PASSWORD` required action from the development accounts in the realm file.
- Leave accounts created through `/api/accounts` unchanged. They keep the temporary-password flow in the Keycloak administrator console.
- Update the Keycloak README to document the password parameter, its override, and sign-in.

Committed configuration still contains no passwords or client secrets.
Password grants stay disabled, and developers still sign in through the browser with authorization code flow and PKCE.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `patient-progress-tracking/identity-and-access`: Local orchestration provides usable credentials for the development accounts from a local secret, without committed passwords.

## Impact

- `tools/AppHost/AppHost.cs`: new `keycloak-dev-password` secret parameter passed to `keycloak-init`.
- `tools/KeycloakInitializer/Program.cs` and `KeycloakRealmReconciler.cs`: read the password and apply it to development accounts that have none.
- `tools/AppHost/Keycloak/Realms/medicalapp-realm.json`: development accounts no longer require a password update.
- `tests/WebApi.IntegrationTests/Common/Infrastructure/Identity/KeycloakContainer.cs` and Keycloak integration tests: tests use the reconciler's password path for development accounts and verify that existing passwords are kept.
- `tools/AppHost/Keycloak/README.md` and `specs/patient-progress-tracking/identity-and-access.md`: documentation of development credentials.
- No API, database, or production configuration changes. Publish mode does not create Keycloak or the parameter.
