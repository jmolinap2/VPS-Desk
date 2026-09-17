# Mobile portability audit

This document tracks the work required to make VPS Desk available on Android without coupling the application core to a specific UI host.

## Current boundaries

- `VpsDesk.Domain`: portable domain model. Must remain platform-independent.
- `VpsDesk.Application`: use cases and remote-operation abstractions. Must remain platform-independent.
- `VpsDesk.Infrastructure`: SSH.NET, SFTP, Linux probes, Docker/deployment services, SQLite and YAML. Expected to be mostly portable, but every dependency must be validated on Android.
- `VpsDesk.Desktop`: Avalonia desktop composition root, localization, desktop views and view models.

## Portability rules

1. Domain and Application must not reference Avalonia, Android, desktop APIs or OS-specific paths.
2. Platform services such as secure storage, local file picking, app-data paths and external launching must be exposed behind interfaces before they are required by shared use cases.
3. Remote SSH/SFTP/Docker/deployment behavior stays in the shared application/infrastructure layers when the underlying library is supported on Android.
4. Desktop and Android remain separate composition roots. Neither host references the other.
5. Shared visual components may be extracted later, after the mobile UX is proven. Full desktop layouts are not a reuse target.

## Android proof-of-concept gates

Before porting the production UI, the Android host must prove:

- the project restores and builds for `net10.0-android`;
- SSH.NET loads correctly on Android;
- password and private-key SSH authentication work against a real VPS;
- host-key/fingerprint validation behaves exactly as on desktop;
- SFTP list/read/write works;
- commands support cancellation and connection failures without blocking the UI;
- Android suspension/resume does not leave reusable services in an invalid state.

## Known high-risk areas

| Area | Risk | Required treatment |
| --- | --- | --- |
| SSH/SFTP | High | Device test before feature UI work |
| Local secrets/private keys | High | Android Keystore-backed implementation; never plain SQLite |
| Deployments | High | Operation lifetime independent from a page/view model |
| Interactive terminal | High | Android-specific terminal input/keyboard surface |
| App lifecycle/network changes | High | Explicit disconnected/connecting/connected/reconnecting/suspended/failed states |
| SQLite path | Medium | Platform-provided app-data path |
| File picker | Medium | Android document picker abstraction |
| Charts | Medium | Validate touch/performance before sharing desktop chart layouts |

## Delivery sequence

1. Establish an Android host project that references Domain, Application and Infrastructure.
2. Add only the platform abstractions demonstrated to be necessary by compilation/runtime tests.
3. Build an SSH/SFTP connectivity spike.
4. Add Android secure storage and app-data providers.
5. Implement mobile shell and Servers.
6. Port Dashboard, Containers, Logs, Security and Storage.
7. Port Deployments with lifecycle-safe execution state.
8. Port Files and Terminal with mobile-specific UI.
9. Add Android CI and release packaging.

The Android project is intentionally a host, not a fork of `VpsDesk.Desktop`.
