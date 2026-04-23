# Launcher Startup Visuals

This supplement records the startup rules that are shared by the launcher and the desktop host.

## Timeout behavior

- `30 seconds` is a soft timeout.
- Soft timeout means `still starting`, not `failed`.
- When the host process is alive or Public IPC is connected, Launcher keeps waiting and avoids launching another host process.
- `120 seconds` is the hard timeout for `desktop_not_visible`.

## Visual mode resolution

- `EnableSlideTransition = true` resolves to `SlideSplash` and forces `EnableFadeTransition = false`.
- `EnableSlideTransition = false` and `EnableFadeTransition = false` resolves to `StaticSplash`.
- `EnableSlideTransition = false` and `EnableFadeTransition = true` resolves to `Fade`.

## Fullscreen splash rules

- Fullscreen splash uses the shared `logo_nightly.png` asset.
- Slide splash enters from the right edge of the target screen and exits back to the right edge.
- Static splash uses the same fullscreen black surface without motion.

## Recovery rules

- Closing Launcher during startup does not cancel the startup attempt.
- Relaunching Launcher attaches to the active attempt instead of spawning a second desktop process.
- If a host process is still alive during failure handling, Launcher offers activation or continued waiting before any retry.
