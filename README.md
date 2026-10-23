# WaiveGK6XPlus

Native Windows keyboard configuration for the 96XS-RGB (model 656801936). Version 0.4.0 uses WPF and the installed .NET 10 Windows Desktop runtime, without Electron or a browser process.

## Open the app

Double-click **Launch.cmd**, or open **dist/WaiveGK6XPlus/WaiveGK6XPlus.exe**. Launch-Native.cmd is a compatibility shortcut to the same app.

Select keys by dragging across the keyboard, then choose a color or effect and click Apply. Ctrl-drag adds/removes keys; Alt-drag removes. Save keeps your profile on this PC. The sidebar switches between live operation and onboard storage. The header button and Windows minimize button hide the app in the system tray and keep it running. Double-click the tray icon to reopen it, or right-click for Open, Stop live profile, and Exit. Close exits.

Profiles are kept in **dist/WaiveGK6XPlus/Profiles**. Keep that folder when updating. Native profile bundles use the .waive extension. Import/export also supports legacy CMF, CMS, simple LE, and GPR files.

## Onboard lighting

Onboard writes now use the firmware's separate erase and data-write acknowledgements and its lighting-effect storage region. The built-in lighting write was physically confirmed: Q–P red, other lights off. A lighting-only write was also acknowledged on Layer 3. Both tests preserved key assignments and macros. Power-cycle persistence has not yet been independently checked.

Profiles without key overrides preserve existing onboard key assignments and macros. Profiles with key/Fn overrides write their complete key configuration; check those assignments before applying. The built-in layer changes lighting only, using all five built-in lighting slots. Key-press glow uses live mode or an editable onboard layer.

## Source and build

- native/: application, device access, protocol/compiler, checks, build script, and usage guide.
- reference/: protocol fixtures, CLI reference, archived recovery source, and captured hardware evidence.
- docs/: design notes and cleanup report.
- Backups/: preserved user data and migration copies. These are local and are not part of the app package.
- .tools/: local development SDK. It is not needed to run the published app.

Build with **powershell -ExecutionPolicy Bypass -File native/Build.ps1**. This runs the regression checks before publishing. See native/README.md for verification details.

The old Electron distribution, extracted dependency tree, obsolete scripts, duplicate releases, and stale test output have been removed. Recovery source and user data were archived first. The original installation under Program Files was not changed.

