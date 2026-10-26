# WaiveGK6XPlus

Native Windows keyboard configuration for the 96XS-RGB (model 656801936). Version 0.4.0 uses WPF and the installed .NET 10 Windows Desktop runtime, without Electron or a browser process.

## Open the app

Double-click **Launch.cmd**, or open **dist/WaiveGK6XPlus/WaiveGK6XPlus.exe**. Launch-Native.cmd is a compatibility shortcut to the same app.

Select keys by dragging across the keyboard, then choose a color or effect. Ctrl-drag adds/removes keys; Alt-drag removes. Apply now tries the profile through the app. Save lighting onboard stores only lighting in the selected keyboard memory slot, preserving key assignments and macros. Save on PC keeps an editable local copy. Key mapping has a separate Save full profile onboard action for lighting, key assignments, and referenced macros.

The Windows minimize button hides the app in the system tray and keeps it running. Double-click the tray icon to reopen it, or right-click for Open, Use onboard settings, and Exit. Close exits.

Profiles are kept in **dist/WaiveGK6XPlus/Profiles**. Keep that folder when updating. Native profile bundles use the .waive extension. Import/export also supports legacy CMF, CMS, simple LE, and GPR files.

## Onboard lighting

Onboard writes now use the firmware's separate erase and data-write acknowledgements and its lighting-effect storage region. The built-in lighting write was physically confirmed: Q–P red, other lights off. A lighting-only write was also acknowledged on Layer 3. Both tests preserved key assignments and macros. Power-cycle persistence has not yet been independently checked.

Save lighting onboard always preserves existing key assignments and macros, even when the edited profile contains key overrides. Save full profile onboard writes key and macro regions when the profile has key/Fn overrides; check those assignments before using it. The built-in slot changes lighting only, using all five built-in lighting slots. Key-press glow needs Apply now or an editable keyboard Layer 1, 2, or 3.

## Source and build

- native/: application, device access, protocol/compiler, checks, build script, and usage guide.
- reference/: protocol fixtures, CLI reference, archived recovery source, and captured hardware evidence.
- docs/: design notes and cleanup report.
- Backups/: preserved user data and migration copies. These are local and are not part of the app package.
- .tools/: local development SDK. It is not needed to run the published app.

Build with **powershell -ExecutionPolicy Bypass -File native/Build.ps1**. This runs the regression checks before publishing. See native/README.md for verification details.

The old Electron distribution, extracted dependency tree, obsolete scripts, duplicate releases, and stale test output have been removed. Recovery source and user data were archived first. The original installation under Program Files was not changed.

