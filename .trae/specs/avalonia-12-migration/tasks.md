# Tasks

- [x] Centralize Avalonia 12 package versions in `Directory.Packages.props`.
- [x] Move the host, Launcher, Plugin SDK, DesktopHost, Shared.Contracts, and Avalonia-facing projects onto central package versions.
- [x] Replace third-party `WebView.Avalonia` usage with official `NativeWebView`.
- [x] Configure WebView2 user data through `EnvironmentRequested`.
- [x] Move FluentAvalonia usages to the FA3 control names and package baseline.
- [x] Move FluentIcons usage to `FluentIcons.Avalonia` and remove the old `.Fluent` package.
- [x] Update Plugin SDK package version and API baseline to `5.0.0`.
- [x] Update plugin runtime shared assembly policy for Avalonia 12 / FluentAvalonia / FluentIcons / Material.
- [x] Fix Avalonia 12 compile breaks in window chrome, binding plugin access, clipboard, bitmap copy, and icon source usage.
- [x] Fix Launcher data location recursion by using a fixed bootstrap config path.
- [x] Fix OOBE state tests and legacy marker compatibility.
- [x] Update PluginTemplate defaults to SDK v5.
- [x] Add SDK v5 migration documentation.
- [x] Update current docs from SDK v4 / Avalonia 11 examples to SDK v5 / Avalonia 12.
- [x] Run full solution tests and record any remaining non-upgrade failures.
- [ ] Perform Windows manual smoke test for host, Launcher, settings, component editor, BrowserWidget, and WebView2 missing-runtime handling.
