# EggIncognito.Runner deploy

Host-side device runner, containerized, deployed as a sidecar via EggIdentity. One container watches every configured device (android over adb, ios via a staged binary), extracts the cleaned proto, posts new-version events, and serves an authed resync/probe API. `network_mode: host`.

Image: `ghcr.io/egginctools/eggincognito-runner:latest`, built by the release workflow. A new `:latest` push redeploys automatically through the stack's pull-and-update pipeline; no app callback.

## Env vars

| Key | Purpose |
|---|---|
| `DEVICES_DIR` | directory of device files; drives the device list |
| `PACKAGE` | default package when a device file omits one |
| `APK_STASH_DIR` | pulled APK / staged binary / per-device state |
| `IOS_BINARY_PATH` | staged Mach-O for ios devices |
| `POLL_INTERVAL` | seconds between full sweeps |
| `PREV_CLIENT_VERSION` | prior client version, for change-detection on first sweep |
| `SYNC_EVENT_URL` / `SYNC_EVENT_SECRET` | new-version ingest endpoint + bearer |
| `RUNNER_TRIGGER_SECRET` | bearer for the resync/extract/probe routes |
| `RUNNER_TRIGGER_URLS` | listener bind |
| `ConnectionStrings__Postgres` | optional; when set the runner owns device probing against the shared DB |
