# EggIncognito (web app)

The ASP.NET Core app. Serves the mock Egg, Inc. API, the Blazor Server UI, and the JSON APIs behind it. Part of [EggIncognito](../README.md).

## Wire format

- Request: `POST /<path>`, body `data=<base64(proto bytes)>`, content type `application/x-www-form-urlencoded`.
- Response: base64-encoded proto bytes. `text/html` on success, `text/plain` on error.
- The real API wraps many responses in an `AuthenticatedMessage` envelope, optionally compressed; the mock returns the inner message directly. Signed requests wrap the inner proto in an `AuthenticatedMessage` with a SHA256-based `code`.

## UI

Blazor Server, InteractiveServer over a SignalR circuit.

- `/protos#api` - the API workbench: build, sign, send, and decode any API request, plus the live capture pane (`#api/capture`).
- `/protos` - proto registry, game-data repository, public data API + key management.

## Run modes

Local (default) has full features. Hosted is the public deploy: capture and shared-data writes return 403, everything read-only stays available.

## Auth

Optional cookie auth backed by the external EggIdentity service; users and roles live there, not locally. API keys authenticate reads and lift the rate tier. Every API action declares an explicit access floor, default-deny.
