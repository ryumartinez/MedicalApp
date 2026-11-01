# Local Keycloak

Aspire starts Keycloak on port `8080` with a persistent data volume.
The local realm is `medicalapp`.
The API starts after the `keycloak-init` resource finishes successfully.
The initializer uses the administrator credential in the AppHost secret store.

## Initialization and reconciliation

On a fresh volume, Keycloak imports `Realms/medicalapp-realm.json`.
On each start, `keycloak-init` reconciles the realm settings, application clients, API roles, and Swagger audience mapper.
It preserves unrelated clients, mappers, accounts, and existing account role assignments.
It imports missing development accounts with their specified subjects and roles.
It does not assign passwords to these accounts.

If a development username already belongs to another subject, initialization fails without changing the account.
Resolve that conflict in the Keycloak administrator console before the next start.
After a configuration change, restart the AppHost to run reconciliation again.
Keycloak startup import alone does not update an existing realm.

The Swagger client uses authorization code flow and PKCE.
Its `basic` and `roles` default scopes add the subject and API roles to access tokens.
The API client defines the `doctor`, `patient`, and `administrator` roles.
Access tokens expire after five minutes.
Refresh sessions expire after 30 minutes of inactivity or eight hours in total.
Keycloak uses persistent user sessions so its idle timeout has no two-minute grace window.
The application clients do not permit offline access.
Signed storage URLs have a separate expiration policy.

## Credentials and state

Aspire stores the generated bootstrap password as `Parameters:keycloak-password` in the AppHost secret store.
Keep this password private.
Keycloak stores the administrator credential in its data volume after the first start.
Changing the bootstrap parameter does not change that stored credential.
If the stored credential changes, update the AppHost secret to match before the next start.

The development doctor and patient accounts require passwords before interactive login.
Set those passwords through the Keycloak administrator console or another trusted provisioning operation.
Never commit passwords or client secrets in the realm file.

`Keycloak:DataVolume` and `Postgres:DataVolume` can select named local volumes.
Changing a volume name selects different data; it does not migrate the existing data.
The default volume names preserve the original local data across restarts.
Do not reset an existing volume as part of reconciliation.

Local import and this initializer do not configure production Keycloak.
Production requires its own realm initialization and HTTPS authority.
