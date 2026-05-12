# Checklist

- [ ] `AppSettingsSnapshot.BackToWindowsButtonDisplayMode` exists and defaults to `IconAndText`.
- [ ] `AppSettingsSnapshot` contains icon source, Fluent icon name, and text icon settings with safe defaults.
- [ ] General > Basic Settings includes one folded back-to-platform button settings expander.
- [ ] The expander includes the display-mode dropdown.
- [ ] The expander includes nested icon source, Fluent icon popup picker, and text icon input controls.
- [ ] The Dock button left icon slot renders either a Fluent icon or custom text.
- [ ] `IconAndText`, `IconOnly`, and `TextOnly` modes update the Dock button live.
- [ ] Icon source, Fluent icon name, and text icon updates refresh the Dock button live.
- [ ] The selected mode is preserved when MainWindow saves app settings.
- [ ] Localization keys exist for zh-CN, en-US, ja-JP, and ko-KR.
- [ ] `dotnet build LanMountainDesktop.slnx -c Debug` succeeds.
