# External IPC Public API Checklist

- [x] Host can expose strong-typed public IPC services.
- [x] External .NET client can connect and call built-in services.
- [x] Host publishes launcher startup and loading-state notifications through routed notify.
- [x] Launcher consumes routed notify instead of the old primary custom named-pipe path.
- [x] Plugin SDK exposes public IPC contribution primitives.
- [x] Plugin runtime can discover and register plugin public IPC services.
- [x] Public catalog includes built-in and plugin-contributed services.
- [x] `catalog.changed` is emitted when new services are added after startup.
- [ ] Add example external client sample.
