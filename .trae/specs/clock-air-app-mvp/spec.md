# Clock Air APP MVP

## Goal

Upgrade the built-in `world-clock` Air APP into a focused clock suite while keeping desktop clock widgets as lightweight launch entry points.

## Scope

- Keep the existing Air APP id `world-clock` for Launcher lifecycle compatibility.
- Use one global Clock Air APP instance for every clock widget entry point.
- Provide four tabs: World Clock, Stopwatch, Timer, and Settings.
- Store Clock Air APP settings independently from desktop widget settings at `AirApps/Clock/settings.json`.
- Follow the host language setting and provide localized text for `zh-CN`, `en-US`, `ja-JP`, and `ko-KR`.

## Behavior

- `world-clock` opens as a standard resizable FluentAvalonia window.
- The default window size is approximately `780x560`, with a minimum of `680x480`.
- World Clock shows local time and a configurable city list.
- Default city list is Beijing, London, Sydney, and New York.
- Users can add, remove, and reorder city entries during the Air APP session; the list persists across restarts.
- Stopwatch supports start, pause, resume, lap, and reset; laps are kept in the current window session, up to 50 entries.
- Timer supports fixed presets, a custom minute duration, start, pause, resume, reset, and a completed state.
- Closing the Clock Air APP stops stopwatch and timer activity.
- Minimizing the window keeps stopwatch and timer activity running.
- Timer completion can activate the Clock Air APP window when the setting is enabled.

## Settings

- Time format: follow system, 24-hour, or 12-hour.
- Show seconds.
- Startup tab: last used tab, World Clock, Stopwatch, or Timer.
- Activate window when timer finishes.

## Out of Scope

- Desktop clock widget visual redesign.
- Alarms.
- Focus mode.
- System notifications.
- Running stopwatch or timer after the Air APP window is closed.
- Third-party plugin Air APP declarations.
