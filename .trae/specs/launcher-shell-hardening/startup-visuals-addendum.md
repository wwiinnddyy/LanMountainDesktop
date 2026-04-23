# Launcher Slow-Startup And Startup Visual Addendum

## New startup timing contract

- `30s` is a soft timeout, not a failure threshold.
- After `30s`, if the desktop process is still alive or Public IPC is connected, Launcher must stay in a waiting state and must not start another host process.
- `120s` is the hard timeout.
- Before returning `desktop_not_visible`, Launcher must attempt one foreground recovery through `ActivateMainWindowAsync()`.

## Startup attempt de-duplication

- Launcher persists the current startup attempt in `%LocalAppData%\LanMountainDesktop\.launcher\state\startup-attempt.json`.
- A second Launcher process must attach to a live pending attempt instead of calling `Process.Start()` again.
- Closing the splash window does not cancel startup; it transitions the attempt into detached waiting and preserves recovery state for the next Launcher run.

## Startup visual modes

- `EnableSlideTransition = true` forces `StartupVisualMode.SlideSplash` and automatically disables fade.
- `EnableSlideTransition = false && EnableFadeTransition = false` resolves to `StartupVisualMode.StaticSplash`.
- `EnableSlideTransition = false && EnableFadeTransition = true` resolves to `StartupVisualMode.Fade`.

## UX safeguards

- If the host process is still alive at failure time, the failure dialog must prefer:
  - `Activate`
  - `Wait`
  - `Open Logs`
  - `Exit`
- Retry is only valid when Launcher is not about to create a duplicate desktop process.

## Launcher coordinator guard

- Startup attempts are now reserved before host launch, so concurrent Launchers cannot all reach `Process.Start()`.
- A live coordinator is identified by `CoordinatorPid`, `CoordinatorPipeName`, and a heartbeat newer than `10s`.
- Secondary Launchers send `activate-desktop` or `attach` to the coordinator pipe and then exit with the coordinator status.
- If Host Public IPC is already available during a normal launch, Launcher activates the existing desktop and does not start a new host process.
- Public shell status now reports tray readiness and taskbar-entry usability separately, allowing Launcher to distinguish "running but hidden" from "not recoverable".
