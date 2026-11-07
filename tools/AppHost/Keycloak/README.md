# Local Keycloak

Aspire starts Keycloak on port `8080` with a persistent data volume.
The local realm is `medicalapp`.
The API starts after the `keycloak-init` resource finishes successfully.
The initializer uses the administrator credential in the AppHost secret store.

## Initialization and reconciliation

On a fresh volume, Keycloak imports `Realms/medicalapp-realm.json`.
On each start, `keycloak-init` reconciles the realm settings, application clients, API roles, and Swagger audience mapper.
It also reconciles the provisioning client, its service account roles, and the provisioning marker attribute.
It preserves unrelated clients, mappers, accounts, and existing account role assignments.
It imports missing development accounts with their specified subjects and roles.
It does not assign passwords to these accounts.

If a development username already belongs to another subject, initialization fails without changing the account.
Resolve that conflict in the Keycloak administrator console before the next start.
After a configuration change, restart the AppHost to run reconciliation again.
Keycloak startup import alone does not update an existing realm.
The reconciliation code is `tools/KeycloakInitializer/KeycloakRealmReconciler.cs`.
Integration tests run the same code against a fresh Keycloak container.

## Clients and tokens

The Swagger client uses authorization code flow and PKCE.
Its only redirect URI is `https://localhost:7255/swagger/oauth2-redirect.html`.
Its `basic` and `roles` default scopes add the subject and API roles to access tokens.
The API client defines the `doctor`, `patient`, and `administrator` roles.
The API reads roles only from `resource_access.medicalapp-api.roles`.
Realm roles, other clients' roles, and Keycloak administrative roles grant no application access.

Access tokens expire after five minutes.
The API validates expiration without clock skew.
Refresh sessions expire after 30 minutes of inactivity or eight hours in total.
Keycloak uses persistent user sessions so its idle timeout has no two-minute grace window.
The application clients do not permit offline access.
Signed storage URLs have a separate expiration policy.

## Issuer configuration

Tokens carry the issuer that the browser used, `http://localhost:8080/realms/medicalapp`.
The API discovers Keycloak through Aspire at the same host address, so browser and API agree on the issuer.
Swagger UI uses that address for its authorization and token URLs.
Set `Authentication:Keycloak:BrowserAuthority` only if the browser must use another address.
The migration service receives the same issuer as `Authentication:Keycloak:DevelopmentIssuer` for development mappings.

## Credentials and state

Aspire stores the generated bootstrap password as `Parameters:keycloak-password` in the AppHost secret store.
Aspire stores the generated provisioning client secret as `Parameters:keycloak-provisioning-secret`.
The initializer applies that secret to the `medicalapp-provisioning` client on each start.
The API receives it as `Authentication:Keycloak:Provisioning:ClientSecret`.
Keep both values private, and never commit passwords or client secrets in the realm file.

Keycloak stores the administrator credential in its data volume after the first start.
Changing the bootstrap parameter does not change that stored credential.
If the stored credential changes, update the AppHost secret to match before the next start.

The development doctor, patient, and administrator accounts require passwords before interactive login.
Set those passwords in the Keycloak administrator console under **Users** > account > **Credentials**.
In development, the migration service links the doctor and patient accounts to seeded records with an active treatment relationship.
The administrator account needs no application mapping.

`Keycloak:DataVolume` and `Postgres:DataVolume` can select named local volumes.
Changing a volume name selects different data; it does not migrate the existing data.
The default volume names preserve the original local data across restarts.
Do not reset an existing volume as part of reconciliation.

## Restart behavior

Restarting the AppHost keeps the Keycloak volume, so accounts, passwords, role assignments, and sessions within their lifetimes survive.
The initializer runs again and changes only managed settings that differ from the realm file.
The migration service skips development mappings that already exist, including deliberately disabled ones.
Application mappings live in PostgreSQL and survive restarts with the PostgreSQL volume.

## Account provisioning

Administrators provision accounts through the `/api/accounts` endpoints with a token that has the `administrator` API role.
The API calls Keycloak through the `medicalapp-provisioning` service account.
That account can manage users and read clients, but it cannot change realm settings or its own roles.
The API never collects passwords.

Keycloak and PostgreSQL do not share a transaction, so the application mapping stays disabled until every step succeeds.
Follow these steps for a new account:

1. `POST /api/accounts` with the username, email, and names.
   The API creates the Keycloak account with a provisioning marker and a disabled mapping.
2. `POST /api/accounts/{id}/links` with an existing doctor or patient ID, or with details for a new record.
3. `PUT /api/accounts/{id}/roles` with the matching `doctor` or `patient` roles.
4. `POST /api/accounts/{id}/enable`.
   The API returns `409` with the remaining steps until links and roles match.
5. In the Keycloak administrator console, set a temporary password for the account and give it to the account holder.

Every step is safe to repeat.
To recover an interrupted provisioning, call `GET /api/accounts/{id}` and repeat the steps that `pendingSteps` lists.
If the mapping ID is unknown, repeat step 1 with the same username.
The API finds the Keycloak account through its provisioning marker and returns the existing mapping, or creates the missing one.

The API refuses usernames and emails that belong to accounts it did not create.
It never links accounts through matching usernames or email addresses.
To bring such an account under provisioning, delete or rename it in Keycloak first, then start again from step 1.
If a Keycloak account is deleted, disable its mapping and provision a new account; a new account receives a new subject.

To disable an account, call `POST /api/accounts/{id}/disable`.
The API disables the mapping first, so the next authorization check denies access.
It then disables the Keycloak account and ends its sessions, so refresh tokens cannot issue new access tokens.
Access tokens that were already issued stay valid for at most five minutes, but the disabled mapping grants nothing.
To restore access, call the enable endpoint again.

## Production

Publish and deploy operations do not create the Keycloak container, import realm files, or run the initializer.
The AppHost adds those resources only in run mode.
Production requires a durable Keycloak service with its own realm initialization.
Configure the API with `Authentication:Keycloak:Authority`, an HTTPS URL that ends in `/realms/medicalapp`.
Startup fails without it, and Aspire `https+http://` discovery addresses do not satisfy it.
Also configure `Authentication:Keycloak:Provisioning:ClientSecret` for the production provisioning client.
The production database, realm initialization, operator access, TLS, hostname, and backups belong to the deployment plan.

To confirm that publication contains no local identity resources, run:

```bash
dotnet run --project tools/AppHost -- --operation publish --publisher manifest --output-path ./publish/aspire-manifest.json
```

The output must not mention `keycloak`, the realm file, or the provisioning secret.
