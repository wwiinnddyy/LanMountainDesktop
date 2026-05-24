# Runtime Packaging Fix Checklist

- [x] `dotnet build LanMountainDesktop.slnx -c Debug -v minimal` succeeds.
- [x] Runtime probe, AirAppHost startup, and packaging policy tests pass.
- [ ] Full `win-x64` package dry run completes without timeout.
