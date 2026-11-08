# Identity - Use ASP.NET Core Identity and OpenIddict instead of Keycloak

- Status: accepted
- Deciders: ryumartinez
- Date: 2026-11-08
- Tags: dotnet, identity, authentication, oauth, openiddict, keycloak, aspire

Technical Story: OpenSpec change `replace-keycloak-with-openiddict`

## Context and Problem Statement

MedicalApp authenticated doctors, patients, and administrators through a local Keycloak container.
That setup needed a 300-line realm reconciler, a realm file, preview-only Aspire packages, a provisioning service account, and an admin REST client.
Account provisioning changed both Keycloak and PostgreSQL without a shared transaction, so the API needed provisioning markers, disabled-until-complete mappings, and retries to stay consistent.
No production Keycloak existed yet.

How should the application authenticate staff-managed accounts and issue tokens for its own API?

## Decision Drivers

- Account provisioning should be one database transaction
- Local startup should need no separate identity service or reconciliation step
- Tokens must stay standard OAuth 2.0 and OpenID Connect, with PKCE, audience, and role checks
- Integration tests should use the real sign-in flow without a slow container
- No license cost

## Considered Options

1. **Keep Keycloak**
2. **ASP.NET Core Identity and OpenIddict inside the WebApi**
3. **ASP.NET Core Identity and OpenIddict in a separate authorization server project**
4. **Duende IdentityServer**

## Decision Outcome

Chosen option: "ASP.NET Core Identity and OpenIddict inside the WebApi", because accounts and mappings then share one database and one transaction, and the API validates tokens with the server's own keys without a back channel.

### Consequences

- ✅ Provisioning creates the account and its mapping atomically. The provisioning marker, service account, and admin REST client are gone.
- ✅ `aspire start` runs no identity container. Integration tests are faster and need no Keycloak container.
- ✅ Disabling an account revokes pending authorization codes and ends sign-in sessions in the same database.
- ❌ The API now hosts sign-in and password pages, so it carries a login attack surface: lockout, antiforgery, and short-lived tokens mitigate it.
- ❌ There is no admin console, email reset, or MFA. Administrators use the account endpoints and single-use password setup links.
- ❌ Production must manage signing and encryption certificates and persistent data protection keys.

## Pros and Cons of the Options

### Keep Keycloak

- ✅ Mature identity product with an admin console, MFA, and federation
- ❌ Two systems to keep consistent during provisioning
- ❌ Preview Aspire integration and an extra JVM service in every environment

### ASP.NET Core Identity and OpenIddict inside the WebApi

- ✅ One database, one transaction, one deployment
- ✅ Standard tokens, PKCE, and audience validation through OpenIddict
- ❌ The API serves HTML sign-in pages alongside its JSON endpoints

### ASP.NET Core Identity and OpenIddict in a separate project

- ✅ Keeps the API a pure resource server
- ❌ Provisioning crosses two processes again and needs an internal admin API

### Duende IdentityServer

- ✅ Full-featured, well-documented framework
- ❌ Commercial license required

## Links

- [Identity and sign-in guide](../../src/WebApi/Features/Identity/README.md)
- [Identity and access spec](../../specs/patient-progress-tracking/identity-and-access.md)
