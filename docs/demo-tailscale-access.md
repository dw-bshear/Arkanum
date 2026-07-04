# Arkanum demo access through Tailscale

This demo instance is running on Mycroft and is intended for temporary colleague demo use.

## Running services

- Arkanum review host: `http://localhost:5068` when accessed through the Tailscale SSH tunnel below.
- Keycloak OIDC lab IdP: `http://localhost:8085` when accessed through the Tailscale SSH tunnel below.
- Arkanum is configured for OIDC mode with the Keycloak lab realm.
- Local emergency access remains enabled.

## Why the tunnel is used

Direct inbound TCP to Mycroft's Tailscale IP was not reachable from another tailnet node during setup, and `tailscale serve` currently requires sudo/operator configuration on Mycroft. For the demo, use Tailscale SSH local port forwarding; the browser still reaches the services through the tailnet, but the URLs are localhost on the presenter laptop.

## Presenter setup

From a laptop that is logged into the same tailnet, run:

```bash
tailscale ssh mycroft -L 5068:127.0.0.1:5068 -L 8085:127.0.0.1:8085
```

Leave that terminal open for the demo.

Then open:

```text
http://localhost:5068/
```

## Demo OIDC account

```text
Username: phase4.user
Password: Phase4User!234
```

This Keycloak user maps to an active non-local Arkanum user with roles:

```text
User,VaultManager
```

## Negative-test OIDC account

```text
Username: unknown.user
Password: UnknownUser!234
```

This account exists in Keycloak but is intentionally not active in Arkanum. It should be rejected and return to the Arkanum login page with a federation error.

## Local emergency access

The Arkanum login page still shows local emergency access. Use the existing local admin credentials if OIDC needs to be bypassed during the demo.

## Current lab configuration

```text
OIDC authority: http://localhost:8085/realms/arkanum
Client ID: arkanum-web
Scopes: openid profile email
Redirect URI: http://localhost:5068/signin-oidc
```

The lab uses HTTP and has insecure OIDC metadata allowed in the review host startup script. Production/ADFS configuration should use HTTPS and trusted certificates.

## ADFS documentation reminder

For likely ADFS deployment docs, capture at least:

- relying party / OIDC client registration steps;
- redirect URI: `https://<arkanum-host>/signin-oidc`;
- required claims: subject/stable identifier, UPN or preferred username, email, display name;
- client secret/certificate handling and rotation;
- PIV/CAC/MFA enforced at ADFS/IdP, not directly by Arkanum by default;
- Arkanum user mapping policy: existing active imported users only unless JIT provisioning is explicitly approved;
- local emergency admin remains available as break-glass access.
