# EggIncognito.Capture

The capture engine. A TLS-intercepting proxy that records real auxbrain.com traffic and turns it into mock-server endpoints. Part of [EggIncognito](../README.md); the web app hosts the dashboard and API in front of it.

## Selective decrypt

Only auxbrain hosts are decrypted. Everything else, including Apple, Google, and certificate-pinned apps, tunnels through untouched, so the rest of the phone keeps working during a capture.

## Manual capture

Start the proxy from the API workbench (`/protos#api`, or launch with `--capture`), point the phone's Wi-Fi proxy at the computer, install and trust the printed root CA, and play. Each captured flow becomes a redacted endpoint on disk, the route map self-repairs, and the session is written to a HAR.

## Device farm

The farm runs with zero on-device taps. It spans rooted Android phones, jailbroken iOS phones, redroid containers, and secondary-user islands on a shared Android host. This project supplies the per-device persistent listeners; the drivers that automate proxy config and CA trust over `adb`/`ssh` live in EggIncognito.Core and the web app, not here. The farm path harvests wire metadata only; the authoritative client version comes from the device binary itself. Off by default.
