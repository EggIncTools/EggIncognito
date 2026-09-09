<h1 align="center">EggIncognito</h1>

<p align="center">
  <a href="https://github.com/EggIncTools/EggIncognito/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/EggIncTools/EggIncognito/ci.yml?branch=main" alt="CI"></a>
  <a href="https://discord.davidarthurcole.me"><img src="https://img.shields.io/badge/discord-join%20server-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
</p>

A toolkit for the [Egg, Inc.](https://auxbrain.com/) API: a stateless mock server that replays captured responses in the exact base64-protobuf wire format, a request inspector, a live capture proxy, a versioned proto registry, and a physical device farm. Test Egg, Inc. tooling without real accounts or rate limits.

Public instance: [eggincognito.davidarthurcole.me](https://eggincognito.davidarthurcole.me).

## Quick start

Self-contained binaries (`linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`) on the [latest release](https://github.com/EggIncTools/EggIncognito/releases/latest). No .NET install. The response set ships alongside the executable.

```sh
docker run -p 5080:8080 ghcr.io/egginctools/eggincognito:latest
```

Or from source:

```sh
dotnet run --project EggIncognito
```

Listens on `http://localhost:5080`. Point your Egg, Inc. client at that address.

## Responses

Endpoint files on disk, grouped by API namespace, keyed by API path:

```
Endpoints/
  default/
    ei/first_contact_secure.json
  eids/
    EI0000000000000001/
      ei/first_contact_secure.json
```

Per-EID endpoint wins, default is the fallback. Files are protobuf JSON in camelCase; an empty or missing file returns all-default field values. With Postgres configured, stored rows overlay the file defaults.

## Capture

The app embeds a TLS-intercepting proxy that records real game traffic into reusable endpoints. It decrypts auxbrain hosts only; everything else tunnels through untouched. Start it from the API workbench (`/protos#api`) or launch with `--capture`.

## Projects

- [EggIncognito](EggIncognito/README.md) - the ASP.NET Core web app.
- [EggIncognito.Core](EggIncognito.Core/README.md) - shared services + proto types.
- [EggIncognito.Capture](EggIncognito.Capture/README.md) - the capture engine.
- [EggIncognito.Data](EggIncognito.Data/README.md) - the optional Postgres layer.
- [EggIncognito.Bot](EggIncognito.Bot/README.md) - the optional Discord bot.
- [EggIncognito.RouteGenerator](EggIncognito.RouteGenerator/README.md) - the controller source generator.
- EggIncognito.GameData - data-free game-data catalogs and effect folding, published as a package.
- EggIncognito.Protos - the content-only `ei.proto` package; EggLedger builds against it.
- EggIncognito.Runner - the device-farm agent.
- EggIncognito.CssBuild - the build-time stylesheet compiler.
- EggIncognito.FixtureSync - build-time refresh of the checked-in wire fixtures from a prod instance; runs only when `EGI_PROD_URL` and `EGI_PROD_API_KEY` are set.
- EggIncognito.RelayAgent - the host-side sidecar for hosted capture routing.

## HTTPS (optional)

Drop `server.crt` and `server.key` in the gitignored `certs/`. Kestrel then binds 8080 and 8443, overridable via `HttpPort`/`HttpsPort`; compose maps those to 5080 and 5443. Without the pair it keeps the default endpoints.
