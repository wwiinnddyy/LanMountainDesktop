# Checklist

- [x] Clicking `DesktopClock` and `DesktopWorldClock` opens the same global Clock Air APP type.
- [x] Repeated `world-clock` open requests use the global `world-clock:clock-suite:global` instance key.
- [x] Whiteboard Air APP keeps its per-component instance key behavior.
- [x] Clock Air APP opens as a normal application window, not a desktop-layer window.
- [x] Clock Air APP settings are independent from desktop clock widget settings.
- [x] Corrupt Clock Air APP settings fall back to defaults.
- [x] World clock time labels support 12-hour, 24-hour, and follow-system formatting.
- [x] Added localization keys are present in all four supported language files.
- [x] Build and automated tests pass.
- [ ] Manual visual verification in all four languages.
- [ ] Manual verification that minimizing keeps stopwatch and timer running while closing stops them.
