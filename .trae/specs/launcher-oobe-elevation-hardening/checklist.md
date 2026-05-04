# Launcher OOBE and Elevation Hardening Checklist

- [ ] New install shows OOBE once.
- [ ] Same-user reinstall does not show OOBE again.
- [ ] `postinstall` launch path is handled without misclassifying the user state.
- [ ] `apply-update` and `plugin-install` do not auto-enter OOBE.
- [ ] Default plugin install does not request UAC.
- [ ] Logs include OOBE status, suppression reason, and launch source.
- [ ] Startup presentation step inside `OobeWindow` (after data location) writes host `settings.json` and syncs Windows Run when autostart is chosen (Launcher executable).
