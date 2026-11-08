# Tasks

## 1. Reconciler password handling

- [x] 1.1 Add a `developmentPassword` argument to `KeycloakRealmReconciler.ReconcileAsync`. Reject values shorter than 8 characters before any Keycloak call, next to the provisioning secret check. Verify that a new integration test gets an `InvalidOperationException` for an empty or 7-character value.
- [x] 1.2 After the user import loop, set the password for each realm-file account that has no `password` credential: use `reset-password` with `temporary = false`, then remove only `UPDATE_PASSWORD` from its required actions (design decision 3). Verify with the tests in 1.4.
- [x] 1.3 Remove `UPDATE_PASSWORD` from the three development users in `tools/AppHost/Keycloak/Realms/medicalapp-realm.json`. Verify that the file contains no `requiredActions` or `credentials` entries for those users.
- [x] 1.4 Add `tests/WebApi.IntegrationTests/Authentication/DevelopmentAccountPasswordTests.cs`, using its own Keycloak container so the shared fixture is unchanged. Cover these cases:
  - An account with no password and with `UPDATE_PASSWORD` gets the password, and the action is removed.
  - An account whose password was changed keeps it after a second reconciliation, and the reconciler password is rejected.
  - A second reconciliation with a different password leaves existing passwords unchanged.
  - An account that is not in the realm file, has no password, and has `UPDATE_PASSWORD` stays unchanged.

  Verify with `dotnet test tests/WebApi.IntegrationTests --filter DevelopmentAccountPasswordTests`.
- [x] 1.5 Update `KeycloakContainer.InitializeAsync` to pass its generated password to `ReconcileAsync` and to stop calling `SetPasswordAsync` for the three development accounts. Keep `SetPasswordAsync` for other test users. Verify that `SwaggerAuthorizationTests` and `KeycloakAuthenticationTests` pass, which shows the development accounts sign in through the reconciler path.

## 2. Local orchestration and documentation

- [x] 2.1 In `tools/AppHost/AppHost.cs`, add the `keycloak-dev-password` parameter inside the local Keycloak block: alphanumeric, 16 characters, `secret: true`, `persist: true`. Pass it to `keycloak-init` as `KEYCLOAK_DEV_PASSWORD`. Verify that the AppHost builds and that publish mode defines neither the parameter nor Keycloak.
- [x] 2.2 In `tools/KeycloakInitializer/Program.cs`, read the required `KEYCLOAK_DEV_PASSWORD` variable and pass it to `ReconcileAsync`. Verify that the project builds and that a missing variable fails with a clear message.
- [x] 2.3 Update `tools/AppHost/Keycloak/README.md`:
  - Replace the instruction to set passwords in the administrator console with the parameter, how to read and override it with `dotnet user-secrets`, and the sign-in usernames.
  - State that a changed secret does not update existing passwords, and how to re-apply it.

  Verify that the README's commands run as written against `tools/AppHost`.
- [x] 2.4 Update the development credentials paragraph in `specs/patient-progress-tracking/identity-and-access.md` to describe the `keycloak-dev-password` local secret. Verify that it still states that committed files contain no passwords.

## 3. Integration check

- [x] 3.1 Run `aspire start` on an existing Keycloak volume whose development accounts have no password. Sign in through Swagger at `https://localhost:7255/swagger` as `development-doctor` with the value from `dotnet user-secrets list --project tools/AppHost`. Confirm that there is no password-change prompt and that a doctor endpoint returns success.
- [x] 3.2 Change `development-patient`'s password in the Keycloak console, restart the AppHost, and confirm that the changed password still signs in. Run `git grep -n "keycloak-dev-password"` and confirm that no committed file contains its value.

## Workflow follow-up

- Archive the change after review, and confirm that `openspec/specs/patient-progress-tracking/identity-and-access/spec.md` includes the three new requirements.
