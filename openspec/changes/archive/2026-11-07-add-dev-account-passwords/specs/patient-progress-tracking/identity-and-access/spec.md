# Spec Delta

## ADDED Requirements

### Requirement: Provide local development account credentials

Local orchestration SHALL give the development doctor, patient, and administrator accounts a usable shared password from a local secret.
The secret MUST persist across restarts, and a developer MUST be able to replace it with a chosen value.
These accounts MUST NOT require a password change at first sign-in.
Committed configuration MUST still contain no passwords.

#### Scenario: Sign in on a fresh environment

- **WHEN** Local orchestration starts with no existing Keycloak state and a developer signs in through Swagger as a development account with the local secret password.
- **THEN** Keycloak completes the sign-in without asking for a new password.
- **AND** The resulting access token carries that account's API role.

#### Scenario: Use a developer-chosen password

- **GIVEN** A developer sets the development password secret to a chosen value before the accounts have passwords.
- **WHEN** Local orchestration starts.
- **THEN** Each development account accepts the chosen value as its password.

#### Scenario: Give passwords to existing accounts without one

- **GIVEN** Existing Keycloak state contains development accounts with no password and a pending password update.
- **WHEN** Local orchestration restarts.
- **THEN** Each of those accounts accepts the local secret password and no longer requires a password update.
- **AND** No Keycloak volume reset is required.

#### Scenario: Keep passwords out of committed files

- **WHEN** The repository's realm and orchestration configuration are inspected.
- **THEN** They contain no development account password or password hash.

### Requirement: Preserve existing development account passwords

Initialization MUST NOT replace or remove a password that a development account already has.
Changing the development password secret MUST affect only development accounts without a password.

#### Scenario: Keep a password that a developer changed

- **GIVEN** A developer changed a development account's password in Keycloak.
- **WHEN** Local orchestration restarts.
- **THEN** The account still accepts the developer's password and rejects the local secret password.

#### Scenario: Change the secret after passwords exist

- **GIVEN** The development accounts already have passwords.
- **WHEN** A developer changes the development password secret and restarts local orchestration.
- **THEN** The accounts keep their existing passwords.

### Requirement: Limit development credentials to local development accounts

Only the development accounts defined in the local realm configuration SHALL receive the development password.
Accounts provisioned through the administrator account endpoints MUST keep their existing password process.
Production configuration MUST NOT define or require the development password secret.

#### Scenario: Leave provisioned accounts unchanged

- **GIVEN** An administrator provisioned an account through the account endpoints and it has no password.
- **WHEN** Local orchestration restarts.
- **THEN** The provisioned account still has no password and its required actions are unchanged.

#### Scenario: Publish without development credentials

- **WHEN** The application is published or uses production configuration.
- **THEN** No development password secret is defined or required.
