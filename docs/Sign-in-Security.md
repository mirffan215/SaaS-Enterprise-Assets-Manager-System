# Sign-in security

Administration → **Sign-in security** (permission *System settings*) controls how the people of a company prove who they are. Everything is per company.

## Two-step verification (authenticator app)

Anyone can turn it on under their profile menu → **Two-step verification**: scan the QR code with Microsoft Authenticator, Google Authenticator, Authy, 1Password or any TOTP app, enter the 6 digits, and save the **10 recovery codes** (shown once; each works one time).

After that, signing in asks for the password and then the code. Facts worth knowing:

- A code works once (a stolen code that was already used is useless) and wrong codes count towards the same lock-out as wrong passwords (5 tries, 15 minutes).
- A recovery code can be typed in place of a code. Using one is recorded in the sign-in history.
- The secret is stored encrypted with the site's data-protection keys. If you restore a company on a *different* installation (different keys), its people must be reset and set up again; the company keeps all its data.
- Company policy: **Optional**, **Required for administrators** (people who manage users or roles, and the Platform Super Administrator) or **Required for everyone**. A person who must use it but has not set it up is taken to the setup page at sign-in and cannot use anything else until done. Required means it cannot be turned off.
- **Reset** (Sign-in security → People) turns it off for someone who lost their phone; they keep their account and password and set it up again.

## Allowed networks

A list of addresses and ranges (`203.0.113.7`, `10.20.0.0/16`, IPv6 `2001:db8::/32`). When the list is not empty, people can sign in only from those addresses, and sessions already open from other addresses are ended within a minute. The page refuses to save a list that does not contain your own address.

Behind a reverse proxy or load balancer the site sees the proxy's address; configure forwarded headers first, or the list will compare the wrong address.

## Single sign-on (OpenID Connect)

Lets people use the company's identity provider. It is the authorization-code flow with PKCE; the ID token's signature, issuer, audience, expiry and nonce are all verified. **Nobody is created automatically**: the e-mail address from the provider must match an existing, active user with access to the company. The optional domain restriction (`example.com`) refuses any other address.

Register this redirect address with the provider: `https://<your host>[:port]/Account/SsoCallback` (the Sign-in security page shows it).

| Provider | Provider address | Notes |
|---|---|---|
| Microsoft Entra ID | `https://login.microsoftonline.com/<directory (tenant) id>/v2.0` | App registration → Web → add the redirect address; Certificates & secrets → new client secret; use the *Application (client) id*. Use your own directory id, not `common`. |
| Google Workspace | `https://accounts.google.com` | Google Cloud → APIs & Services → Credentials → OAuth client (Web application). Set the domain restriction to your Workspace domain. |
| Others (Okta, Keycloak, …) | the *issuer* URL | Must publish `/.well-known/openid-configuration` over https. |

People who sign in with single sign-on are not asked for the app code: the provider's own second step applies. The address list still applies.

## If a company locks itself out

Operator page (platform console) → **Emergency** next to the company: clears its address list, two-step requirement and single sign-on, unlocks the first administrator (`admin`), resets their two-step verification and ends their sessions. Passwords are not touched. The action is recorded in the operator log.

## Not included (yet)

Enforcing single sign-on (hiding the password form), SCIM provisioning, per-user API keys and webhooks, and WebAuthn/passkeys.
