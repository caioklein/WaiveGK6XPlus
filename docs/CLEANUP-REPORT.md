# WaiveGK6XPlus 0.4.0 cleanup

Completed 23 October 2026 after the native build and hardware lighting tests.

The project now has one published application: `dist/WaiveGK6XPlus/WaiveGK6XPlus.exe`. `Launch.cmd` opens it; `Launch-Native.cmd` remains a compatibility shortcut.

Removed the old Electron distribution, extracted dependencies, duplicate native releases, obsolete recovery/build scripts, stale screenshots and temporary test files. Generated native bin/obj folders were removed after the final package passed verification; the build script recreates them.

Before removal, the following archives were created and verified by entry count and decompression:

| Archive | Contents | Files | Size |
| --- | --- | ---: | ---: |
| reference/legacy-electron-source.zip | Recovered source, resources and recovery scripts, excluding reinstallable node_modules | 4,658 | 88.18 MiB |
| Backups/legacy-user-data.zip | User data, profiles and diagnostics from older distributions | 357 | 1.09 MiB |
| reference/development-captures.zip | Hardware traces and prior verification reports | 66 | 0.06 MiB |

The initial cleanup removed 502.5 MiB and retained 89.33 MiB in archives, saving about 413 MiB. The final generated-file cleanup removed another 9.53 MiB, for about 423 MiB saved in total. Archive inventories and removal totals are recorded in `Backups/archive-manifest.json` and `Backups/cleanup-summary.json`.

Native profiles and diagnostics were also copied to `Backups/Profiles` and `Backups/Diagnostics`, then migrated into the current published app folder. Keep its Profiles folder when updating.

Kept the native source, protocol fixtures, license notices, design notes, CLI reference and local SDK. The SDK under `.tools` is used for development and is not bundled with the app. The original Program Files installation was not modified.

Final verification: 201 C# checks, 37 packaged UI checks, successful keyboard identification, and the physically confirmed built-in Q–P lighting test. Latest build and UI reports remain in `native/artifacts/release-040-*`.

