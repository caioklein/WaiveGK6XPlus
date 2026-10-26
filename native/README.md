# WaiveGK6XPlus 0.4.0

An English-only native WPF editor for the confirmed 96XS-RGB, model 656801936, firmware 4.5. Requires .NET 10 Windows Desktop runtime, already installed on this PC. The published folder contains no Electron, Chromium, Node.js, or third-party runtime packages.

## Launch and background operation

Use Launch.cmd or dist/WaiveGK6XPlus/WaiveGK6XPlus.exe. Keep the published folder together. Close other keyboard utilities before connecting. The normal Windows minimize button hides the app in the system tray, removing it from the taskbar. App-controlled lighting continues; UI animation previews stop while minimized. Normal Close exits and returns to the selected keyboard memory slot.

The tray icon is always available while minimized. Double-click it to reopen the app; right-click for Open, Use onboard settings, and Exit. Always show tray icon also keeps it visible while the window is open. Relaunching the app restores its existing hidden window. Start with Windows is opt-in and opens minimized. The existing startup entry is migrated when its verified executable path belongs to the renamed app. Startup/reconnect never automatically applies a profile or plays a macro.

## Colors and effects

Drag across keys to select, Ctrl-drag to add/remove, or Alt-drag to remove. Shift-click selects a range. Dragging may start between keys, catches keys crossed by fast movement, and ends on release or window deactivation. Pick a shade in the color square, change hue on the rainbow strip, or use a swatch/hex value. Arrow keys adjust the picker; Ctrl+arrows adjust hue. A continuous color gesture is one undo step.

Solid, Rainbow, Breathing, and Key-press Glow presets target selected keys or the whole keyboard. Groups can keep separate rainbow/breathing rules. Painting part of a quick preset restores just those keys to solid colors. Painting an imported custom timeline switches back to per-key colors and keeps the old timeline in the library. More key-press effects selects imported/custom glow effects. The advanced lighting editor has its own visual color picker.

The keyboard preview and all lighting payloads use LED locations, separately from switch indices. For example Q is switch 26 and LED 46; Q–P occupy LEDs 46–55. Key bindings and key-press triggers retain logical switch indices. Native effects record Logical/Led coordinate space; original LE files use LED locations and are not translated twice. Older full-matrix/quick-zone effects are recognized when loaded, without rewriting the source files.

## Save, Apply, and onboard storage

Save on PC writes a complete editable bundle to Profiles beside the executable. Previous saves are preserved as .bak; Delete saved copy renames files to .deleted-... for recovery. Unreadable files are reported and kept.

Apply now always uses app control, regardless of the mode recorded in an older profile. The app must stay running or minimized to the tray. Use onboard settings stops app control and returns to the selected keyboard memory slot.

Save lighting onboard writes only colors, effects, and key-press lighting to that slot. It uses a temporary copy without key assignments, Fn assignments, or macros, so those keyboard regions are preserved. The edited profile and its PC copy retain those assignments. The app can be closed after an onboard save.

Save full profile onboard, in Key mapping, also writes key assignments and referenced macros when the profile has key overrides. It requires an editable Layer 1, 2, or 3. Fn shortcuts use this action; website/file/program actions require Apply now. Older profile target modes are migrated to the memory-slot selection when loaded, without rewriting the original file.

The onboard-write fix uses configuration mode for host-report setup, accepts firmware 4.5's zero-status erase reply, and checks data-write status 1. Flash replies identify the opcode rather than echoing the layer number. A rejected write now stops immediately with its actual status, rather than timing out waiting for an echoed layer.

Lighting is stored in region 6, including static RGB frames. The unsupported legacy static buffer is not used. Built-in mode writes lighting only and populates all five selectable lighting slots. A profile without key/Fn overrides preserves key maps and macros; a profile with overrides writes those regions too. Built-in mode cannot remap keys or assign key-press effects. Use live mode or an editable onboard layer for those features.

The entire plan is compiled and checked before writing. An interrupted flash operation may still leave partial data: reconnect and reapply a complete profile. Full flash readback, transactional rollback, and recovery of previous keyboard flash contents are not established. Local profile backups are not hardware flash backups.

The built-in Q–P red profile was stored successfully and visually confirmed by the user. Layer 3 lighting-only writes also completed, preserving key and macro regions. A power-cycle test remains outstanding. Current hardware traces and older diagnostic evidence are preserved in reference/development-captures.zip, with reusable ACK fixtures in reference/native-flash-replies.json.

## Other features

Normal/Fn remapping, modifier shortcuts, media keys, disabled keys, native saved profiles, legacy import/export, macro recording/editing, repeat/hold/toggle playback, and live website/file/program shortcuts are included. Fn remapping uses editable onboard layers. F12 stops explicit macro recording; hooks run only while recording. Stop/exit cancels macros and releases held injected keys/buttons. Imported actions do not execute until a live profile is applied and its assigned key is pressed.

Onboard macros allow 125 events and 16 referenced macros per profile; longer macros can run live. Stored lighting is limited to 128 KiB. Static live profiles have no frame loop; key-triggered lighting sleeps until triggered. Active live effects use sequential frames capped at 10 per second, without a backlog. Grouped/music/game effects, OLED/mouse configuration, and firmware updating remain outside this release.

## Build and checks

Run powershell -ExecutionPolicy Bypass -File native/Build.ps1. It uses the local SDK under .tools when present, or the system SDK, restores with no external package sources, runs the C# checks, and publishes into dist/WaiveGK6XPlus. Development SDK/bin/obj folders are not part of the runtime package.

201 C# checks cover original packets, Windows framing, captured live/flash replies, file codecs, corrupt input, selection geometry, all 103 displayed key locations, Q–P payloads, base/Fn macro indices, size limits, write failure handling, mode transitions, and cancellation cleanup. Temporary check directories are removed after each run.

All 44 packaged UI checks passed, including separate save destinations, lighting-only preservation of bindings, full-profile bindings, and native minimization to the tray. The minimized sample measured about 0.08% of one CPU core over 20 seconds after exercising the editors (about 163 MiB private memory and 213 MiB working set; this is not a clean-start memory baseline).

Read-only UI checks: WaiveGK6XPlus.exe --smoke <output-folder>. Add --idle-smoke for clean startup without opening editors. Smoke mode never clicks Apply or writes onboard settings. It exercises selection, colors/effects, undo, mapping, branded UI, theme templates, dropdown opening, and system-tray minimization and restoration. Reports stay local and may include device identifiers; physical key-report notifications are not logged. Latest reports are in native/artifacts/ui-review-smoke.

Old Electron source is archived in reference/legacy-electron-source.zip without reinstallable dependencies. User data was preserved in Backups/legacy-user-data.zip before duplicate distributions were removed. See docs/CLEANUP-REPORT.md.




