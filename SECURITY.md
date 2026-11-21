# Security Policy

## Supported versions

Security fixes go to the latest release. KZ Companion has not published v1.0.0 yet.

## Reporting a vulnerability

Please **do not** open a public issue for a security problem. Use GitHub's private reporting:
<https://github.com/Killzo1602/KZCompanion/security/advisories/new> (Security tab, "Report a vulnerability").

Include the KZ Companion version, what an attacker could do, and steps to reproduce. Do not include
real credentials, pairing keys or tokens. This is a small project, so there is no guaranteed response time.

## Security model, in short

- The phone talks to the PC over your local network using a single API key generated on the PC and passed
  through the pairing QR code. Anyone who has the key can control the PC through KZ Companion.
- Traffic is plain HTTP/WebSocket on the LAN. There is **no encryption in transit and no end-to-end
  encryption**. Use trusted networks only.
- The API key and integration credentials are stored in `%LocalAppData%\KZCompanion`, encrypted with Windows
  DPAPI for the current user. Other programs running as the same user can read them.
- Actions that run code on the PC are limited to the local PC.
- Known gap: any paired phone can still change some integration settings. Moving credential entry to the PC is planned.

## Handling your own secrets

Never paste pairing keys, QR codes, API keys, client secrets or tokens into issues, logs or screenshots.
If you shared one by accident, remove the pairing on the PC and re-pair, and rotate the affected
integration credential in the service's own dashboard.
