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

## Launcher splash image rules

- The hidden launcher debug menu can save a custom splash image.
- The selected image is copied into the Launcher data directory as `Launcher Picture.<ext>`.
- Supported formats are `.png`, `.jpg`, `.jpeg`, `.bmp`, `.gif`, and `.webp`; files larger than `10MB` are rejected.
- Splash displays the image with `Uniform` fitting, preserving the full image and allowing black letterboxing.
- The splash window uses a transparent self-drawn shell with a fixed Fluent `8px` outer corner radius.

## Recovery rules

- Closing Launcher during startup does not cancel the startup attempt.
- Relaunching Launcher attaches to the active attempt instead of spawning a second desktop process.
- If a host process is still alive during failure handling, Launcher offers activation or continued waiting before any retry.
