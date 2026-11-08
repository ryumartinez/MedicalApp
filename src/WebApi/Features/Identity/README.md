# Identity and sign-in

The API is its own authorization server.
ASP.NET Core Identity stores accounts, password hashes, and API roles in the application's PostgreSQL database.
OpenIddict issues OAuth 2.0 and OpenID Connect tokens, and OpenIddict validation checks them on every API call.
Application access still comes from the `ApplicationIdentities` mappings, which link an issuer and subject to doctor and patient records.

## Endpoints

| Path | Purpose |
|---|---|
| `/.well-known/openid-configuration` | Discovery document and signing keys |
| `/connect/authorize` | Authorization code flow with PKCE (`S256` only) |
| `/connect/token` | Exchanges an authorization code for tokens |
| `/connect/endsession` | Ends the sign-in session |
| `/account/login` | Sign-in page |
| `/account/set-password` | Sets a password from a setup link |
| `/account/logout` | Signed-out confirmation |

The connect endpoints are in `Connect/`, and the pages are in `Pages/Account/`.
There is no registration page.
The password, implicit, client credentials, and refresh token flows are off.

## Clients, scopes, and roles

The migration service creates or updates these in every environment (`IdentityClientInitializer`):

- The `medicalapp-api` scope, with the `medicalapp-api` resource. Tokens requested with this scope carry the `medicalapp-api` audience.
- The `doctor`, `patient`, and `administrator` roles.
- The public `medicalapp-swagger` client, when `Authentication:Clients:Swagger:RedirectUris` is configured. Otherwise the initializer creates no client and leaves an existing one unchanged.

It leaves other clients, accounts, and role assignments unchanged.
Access tokens carry the account's API roles in the `role` claim.
The API reads roles only from validated access tokens, and only the three role names above.

## Tokens and sessions

- Access tokens expire after five minutes. The API checks expiration without clock skew.
- Access tokens are encrypted. Treat them as opaque. Only the API reads them.
- Authorization codes expire after five minutes and work once.
- The sign-in cookie expires after 30 minutes without activity and after 8 hours in total. It is used only by the sign-in pages and connect endpoints. API endpoints ignore it.
- Five failed sign-ins lock an account for 15 minutes. Every failure shows the same message.
- Passwords need at least 12 characters, with no other composition rules.

## Issuer

`Authentication:Issuer` is the API's public base URL, for example `https://api.example.com/`.
Production requires it, and it must be an HTTPS URL without query, fragment, or user information.
In local orchestration, the AppHost sets it to the API's HTTPS endpoint, `https://localhost:7255/`.
Without Aspire, Development uses that address by default.
The migration service gets the same value as `Authentication:DevelopmentIssuer` for the seeded development mappings.

## Local development

`aspire start` creates everything. No manual identity step is needed.
Sign in through Swagger at `https://localhost:7255/swagger` with one of the development accounts:

| Username | Role | Mapping |
|---|---|---|
| `development-doctor` | `doctor` | Seeded doctor, treating the seeded patient |
| `development-patient` | `patient` | Seeded patient |
| `development-administrator` | `administrator` | None needed |

They share one password, which Aspire generates on the first start: 16 letters and digits, kept in the AppHost secret store.
To read it, run:

```bash
dotnet user-secrets list --project tools/AppHost
```

Use the `Parameters:dev-account-password` value.
To choose your own password, at least 12 characters, run this before the accounts have passwords:

```bash
dotnet user-secrets set "Parameters:dev-account-password" "<your-password>" --project tools/AppHost
```

The migration service sets the password only on development accounts that have none.
A changed secret does not replace existing passwords, including passwords that you set through a setup link.
To change a development password, create a setup link for it as described below.
Accounts provisioned through `/api/accounts` never get the development password.

Restarts keep accounts, passwords, roles, mappings, and client registrations in PostgreSQL.
Development signs tokens with OpenIddict development certificates in your user certificate store, so tokens issued before a restart stay valid until they expire.

### macOS certificate store

If the development certificates cannot be created or read from the keychain, create a self-signed certificate for each purpose, using the commands in the Production section.
Store them in the WebApi user secrets:

```bash
dotnet user-secrets set "Authentication:Certificates:Signing:Path" "<absolute-path>/signing.pfx" --project src/WebApi
dotnet user-secrets set "Authentication:Certificates:Signing:Password" "<password>" --project src/WebApi
dotnet user-secrets set "Authentication:Certificates:Encryption:Path" "<absolute-path>/encryption.pfx" --project src/WebApi
dotnet user-secrets set "Authentication:Certificates:Encryption:Password" "<password>" --project src/WebApi
```

Configure both certificates. Development then uses them instead of the development certificates.

### Moving from Keycloak

On the first start after the move, the migration service changes the seeded development mappings to the new issuer. Their links and enabled state stay the same.
Accounts that were provisioned against Keycloak have no sign-in account here. Provision them again, or drop the local database with the AppHost's drop database command.
The `keycloak-dev-password` and `keycloak-provisioning-secret` user secrets are no longer used.
The Keycloak Docker volume is left in place, so you can still go back. When you no longer need it, find and remove it:

```bash
docker volume ls --filter name=keycloak
```

```bash
docker volume rm <volume-name>
```

## Account provisioning

Administrators provision accounts through the `/api/accounts` endpoints, with an access token that has the `administrator` role.
The API creates the sign-in account and its disabled mapping in one database transaction, so a failed request leaves neither.

1. `POST /api/accounts` with the username, email, and names.
   The account has no password, and the mapping is disabled.
2. `POST /api/accounts/{id}/links` with an existing doctor or patient ID, or with details for a new record.
3. `PUT /api/accounts/{id}/roles` with the matching `doctor` or `patient` roles.
4. `POST /api/accounts/{id}/enable`.
   The API returns `409` with the remaining steps until links and roles match. A missing password does not block this step.
5. `POST /api/accounts/{id}/password-setup-link`.
   Give the returned `url` to the account holder through a trusted channel. The holder chooses a password at that link.

Every step is safe to repeat.
`GET /api/accounts/{id}` shows the remaining `pendingSteps` and whether a password is set.
Repeating step 1 with the same username returns the existing mapping.
The API refuses usernames and emails of accounts that have no mapping, such as the development administrator.

### Password setup links

- A link works once and expires after `Authentication:PasswordSetup:Lifetime`, 24 hours by default. The value is a time span, such as `12:00:00`.
- Issuing a new link invalidates earlier unused links and ends the account's sign-in sessions.
- Setting a password ends the account's other sign-in sessions.
- The API response contains no password. The page sends no referrer, and request telemetry omits the link's query string.

### Disabling an account

`POST /api/accounts/{id}/disable` disables the mapping first, so the next authorization check denies progress access.
It then disables sign-in, ends the account's sign-in sessions, and revokes its pending authorization codes.
Access tokens that were already issued stay valid for at most five minutes, but the disabled mapping grants nothing.
To restore access, call the enable endpoint again. Revoked codes stay revoked.

## Production

Production needs these settings:

| Setting | Value |
|---|---|
| `Authentication:Issuer` | The API's public HTTPS base URL |
| `Authentication:Certificates:Signing` | One or more PFX certificates, each with `Path` or `Base64` and an optional `Password` |
| `Authentication:Certificates:Encryption` | One or more PFX certificates, in the same format |
| `Authentication:Clients:Swagger:RedirectUris` | Optional. Set it only if Swagger UI should sign in |

Startup fails if the issuer or a certificate is missing.
Data protection keys are stored in the `DataProtectionKeys` table, so every instance accepts the sign-in cookies, setup links, and codes that another instance created.

To create a self-signed certificate, run these commands for `signing` and again for `encryption`:

```bash
openssl req -x509 -newkey rsa:3072 -sha256 -days 365 -nodes -subj "/CN=MedicalApp signing" -keyout signing.key -out signing.crt
```

```bash
openssl pkcs12 -export -inkey signing.key -in signing.crt -out signing.pfx -passout pass:<password>
```

To rotate the signing certificate, list the new certificate first and keep the old one second, for example `Signing:0` and `Signing:1`.
New tokens are signed with the first certificate, and tokens signed with the old one stay valid.
Remove the old certificate after the access token lifetime has passed.

### First administrator

Run this once against the production database, with the production `ConnectionStrings:AppDb` and `Authentication:Issuer`:

```bash
dotnet run --project tools/MigrationService -- bootstrap-admin --username <username> --email <email>
```

The command creates an administrator account and prints its password setup link to the console only, never to logs or telemetry.
It refuses to run while an enabled administrator exists. Add `--force` to create another one.

To confirm that publication contains no local identity resources, run:

```bash
dotnet run --project tools/AppHost -- --operation publish --publisher manifest --output-path ./publish/aspire-manifest.json
```

The output must not mention `keycloak`, `dev-account-password`, or a development redirect URI.
