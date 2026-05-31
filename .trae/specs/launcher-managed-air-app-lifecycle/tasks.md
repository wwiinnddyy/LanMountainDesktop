# Tasks

> Superseded by `.trae/specs/air-app-runtime-container/`; the checked items below describe the former Launcher-managed implementation.

- [x] Add shared Air APP lifecycle IPC contracts.
- [x] Add Launcher Air APP lifecycle service and dedicated IPC host.
- [x] Make Launcher remain alive while desktop or Air APP processes exist.
- [x] Route desktop Air APP launch requests through Launcher IPC.
- [x] Add hidden `air-app-broker` Launcher command for direct-host development fallback.
- [x] Make desktop fallback start `air-app-broker --requester-pid <pid>` instead of normal `launch`.
- [x] Add broker lifetime and command recognition tests.
- [x] Add AirAppHost registration and unregister best-effort calls.
- [x] Add lifecycle service and request-building tests.
