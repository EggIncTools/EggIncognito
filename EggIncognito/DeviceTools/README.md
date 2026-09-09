# DeviceTools

On-device native tooling for the jailbroken iOS capture and extraction devices, part of the app project. The frida scripts are embedded resources served through a typed accessor. The Theos tweaks build inside a pinned Docker image, so no host needs a Theos install. `eggupdate` drives a headless App Store update of Egg, Inc. by reusing the phone's own logged-in StoreServices session, gated behind an arming flag. `egiuinav` drives synthetic touch and HID events plus screen captures over a file-based command channel under `/tmp`.

- Reusing the device's StoreServices session is what clears the auth wall an external downloader hits.
- Tweak toolchain versions are pinned by `ARG` values in the Dockerfile, not by anything installed on a host.
