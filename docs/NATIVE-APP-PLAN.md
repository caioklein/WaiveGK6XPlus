# Native keyboard app implementation plan

Status: WaiveGK6XPlus WPF 0.4.0 is published to dist/WaiveGK6XPlus. Branding, themed controls, taskbar minimization, and onboard lighting fixes are complete. The user confirmed the built-in Q–P red test; Layer 3 lighting writes also succeeded. Key assignments and macros were preserved. Power-cycle persistence remains untested. Earlier paths and milestones below are historical; see native/README.md for current behavior.
Date: 23 October 2026.

## Outcome and platform choice

Build a modern English-only Windows application for keyboard configuration that does not require Electron, Chromium, or Node.js at runtime. Make persistent onboard settings the default; run background work only for explicitly enabled live features.

Assume Windows x64 first, matching the current installation. Recommend C# on the supported .NET 10 LTS line, WPF for the interface, and Windows HID access for the device layer. Use the latest supported servicing release when implementation begins. WPF provides a native Windows desktop UI; .NET 10 is supported through November 2028. WPF is Windows-only, so a future macOS/Linux requirement would warrant revisiting the UI framework before building the shell.

Official references:
- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Windows HID architecture](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-architecture)
- [.NET distribution options](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

Tauri is a reasonable alternative if cross-platform delivery becomes a requirement, but it still uses a system webview. For this Windows-focused project, a native WPF shell avoids that browser layer. See [Tauri process model](https://v2.tauri.app/concept/process-model/).

The .NET 10 SDK is now installed locally under .tools/dotnet for development. The packaged framework-dependent application uses the installed .NET Windows Desktop runtime.

## What the investigation already provides

- Recovered original JavaScript, device definitions, graphics, key maps, codecs, lighting engine, and macro engine.
- Original installed archive preserved independently of the patched copy.
- 14 original packet fixtures: reference/protocol-vectors.json.
- 275 model-list entries and capability hints: reference/device-inventory.json. Entries are not certified supported hardware.
- CLI reference checked out at reference/gk6ctl, pinned to commit 6d026860c47bdbda47983593697c1a383d111916.
- 11 common packet vectors matched gk6ctl; profile, light, and macro codecs produced identical bytes for synthetic fixtures and decoded each other's output.
- Detailed findings and uncertainties in docs/PROTOCOL-AND-CLI-REVIEW.md.

The cached profile directory suggests model 656801936, identified by the original model list as 96XS-RGB. The first native read-only handshake has now confirmed this connected model, firmware 4.5, and a 14 × 8 matrix.

Most needed logic is readable JavaScript. Further executable/native-binary decompilation is not needed unless a specific required behavior cannot be explained by those sources. Standard HID and input-hook bindings should be replaced with native platform access, rather than porting their Node binary wrappers.

## Scope and compatibility

First usable release:
- Device detection, identification, reconnection, and useful connection errors.
- Visual keyboard with per-key selection and RGB colors.
- Validated onboard profile application and layer selection.
- Normal and Fn key remapping, shortcuts, and media keys where supported.
- Local profiles, import/export, backups, and recovery instructions.
- Built-in lighting controls and firmware-supported stored effects.
- English labels, contextual help, keyboard navigation, and DPI scaling.

Complete core-functionality milestone:
- Macro editing, recording, repeat/stop modes, and onboard playback.
- Lighting-library editing, animation preview, and compatible stored effects.
- Optional live lighting, key-triggered effects, and host-dependent actions.
- Tray controls and optional start with Windows.

Separate investigation:
- Grouped/synchronized lighting, music effects, and unusual model features.
- OLED or mouse support if actually needed.
- Firmware updating: the recovered firmware module is empty. The CLI writes configuration flash, not firmware images; it does not supply a firmware updater.
- Game integration: the recovered API returns an empty list.

Feature parity means matching a documented, working behavior on tested hardware. Do not reproduce an original bug merely to match its output. Keep original-compatible output fixtures separate from corrected behavior fixtures.

## Proposed structure

One desktop process, with independent libraries:

- Waive.Core: typed profiles, key assignments, RGB values, effects, macros, model capabilities, and validation. No UI or HID dependencies.
- Waive.Protocol: binary commands, response parsing, checksums, profile-to-device compilation, and legacy codecs. Testable without hardware.
- Waive.Device.Windows: HID discovery and asynchronous I/O, hotplug events, handles, report framing, and per-device request serialization.
- Waive.Application: apply jobs, profile repository, imports, live playback, recording lifecycle, and connection state.
- Waive.Desktop: WPF views and view models, keyboard drawing, dialogs, accessibility, and tray controls.
- Waive.Cli: optional diagnostic/build/apply tool sharing the same core; not a second protocol implementation.
- Tests: golden packets, codec fixtures, simulated transport, fault injection, and opt-in hardware acceptance.

Normal flow: editor → validated profile → compiled write plan → device queue → acknowledged completion → UI result.

The UI must never issue raw HID commands or depend on model-specific byte offsets.

## Delivery sequence and exit criteria

### 0. Freeze references and establish the target

Preserve the current app, source hashes, relevant device data, and gk6ctl commit. Separate copied upstream code from new code and retain applicable MIT notices. Measure the current app's startup, full process-tree memory, idle CPU, and Apply duration in the actual user session.

Confirm hardware identity, firmware, matrix, HID collection, and capability flags. Back up saved files; do not describe local-file backup as reading the keyboard's current flash.

Exit: a confirmed target device, repeatable baseline, a source map, and a feature checklist identifying host-dependent versus persistent settings.

### 1. Port and test the protocol independently

Implement packet construction, original CRC behavior, report-ID framing, response parsers, key encoding, and profile/light/macro decoding.

Use the original fixtures and CLI comparisons as tests. Add malformed file, truncated response, invalid index, negative ACK, wrong collection, and out-of-order reply tests. Explicitly check all lengths and storage limits before touching the device.

Exit: reference vectors match; unsupported/corrupt data is rejected; no hardware writes are needed for these tests.

### 2. Deliver device discovery and the first lighting change

Implement Windows HID collection filtering, read-only handshake, one request at a time, deadlines, cancellation, disconnect recovery, and diagnostics.

Build a minimal native window showing the detected device and a color picker. Apply a controlled RGB change with progress and explicit success/failure. Test both temporary live colors and the appropriate persistent path for the confirmed model.

Exit: red, green, and blue appear on the physical keyboard; errors do not freeze the app; the UI remains responsive. This is the first usable prototype and the first major decision gate.

### 3. Deliver onboard profiles and remapping

Build a keyboard layout from model data, including special keys and Fn restrictions. Add select-all/multi-select, undo/redo, key bindings, layer controls, persistence, import/export, and an apply review showing changed settings.

Show separate states: edited, saved locally, applying, acknowledged by device, and hardware-tested. Do not claim readback verification without a supported readback command.

Exit: remapping, colors, Fn assignments, and layer selection work on the target; settings persist through app exit and keyboard power cycle where firmware supports persistence; imports cannot silently overwrite the only backup.

### 4. Deliver macros and stored lighting

Port codecs and compilers with corrected indexing and limits. Add macro recording/editing, repeat behavior, animation editing, and preview. Clearly identify effects or actions that require the app.

Record only during an explicit recording session. Stop hooks and release held keys/buttons on stop, disconnect, or error.

Exit: supported macro and lighting samples behave correctly, including Fn bindings where allowed, cancel/stop behavior, maximum-size validation, and legacy file round trips.

### 5. Add optional live features without idle overhead

Implement live rendering and host-dependent actions with a bounded scheduler. Keep essential commands ordered; replace stale preview frames rather than accumulating them. Stop rendering when effects stop, hardware disconnects, or live mode ends.

Only maintain keepalive and device key reports while live mode needs them. On a normal exit from live mode, disable reports and select the user's chosen onboard layer. Handle crashes on the next connection; do not promise cleanup after a terminated process.

Exit: live effects work, queued frames remain bounded, repeated reconnects recover, and onboard-only use needs no background process.

### 6. Polish and release

Finish English copy, light/dark styling, accessible names, focus navigation, high-DPI layouts, progress/error messages, and local diagnostics export.

Publish Windows x64 builds. Offer a small framework-dependent package and a self-contained option; report shared-runtime costs separately. Preserve profile schema versions, migrations, backups, and rollback to the old app.

Exit: clean-machine launch, target-device acceptance, reconnect/sleep/resume tests, performance measurements, and a release checklist with explicit device/feature support.

## Performance targets

Background operation is a primary requirement: the app must remain minimized in the Windows taskbar with low CPU and RAM use. Minimizing must stop UI previews and animations, release disposable editor caches, and preserve only the device work needed by the selected mode. Restoring the window must preserve edits. Tray-only operation may be optional; minimizing must not force it.

WPF is a provisional productivity/performance choice, not a claim of the lowest possible memory footprint. Measure the native prototype while visible, minimized, onboard-only, and running live lighting before committing to the full editor. If its measured background footprint is unsuitable, compare a smaller native Windows implementation before extending the UI. Include all processes and shared-runtime accounting in the comparison.

These are initial goals, not measured results:
- At least 50% less idle private memory than the original full process tree on the same PC.
- Near-zero idle CPU in onboard-only mode; no permanent rendering timer or unnecessary keepalive.
- Visible response to UI edits within about 100 ms; no device I/O on the UI thread.
- Interactive first window within 1.5 seconds on the target PC under a defined startup test.
- Smaller installed footprint than the current roughly 360 MiB installation, counting bundled runtime consistently.
- Bounded live-frame queue and no growth after repeated Apply/reconnect cycles.

Measure device-write duration separately. Firmware limits, packet pacing, and flash latency constrain it; changing the UI framework does not make those operations instantaneous.

## Reliability rules

- Validate and compile the entire write plan before any erase/clean operation.
- Serialize per device and distinguish status notifications from command replies.
- Validate report lengths and command-specific acknowledgements; do not assume every response uses the same ACK field.
- Cancel safely between operations. Configuration flash is not proven transactional, so failures must explain partial writes and how to retry a complete known-good profile.
- Do not automatically replay potentially destructive commands after an ambiguous timeout.
- Expose supported capabilities per model; original hardcoded matrix sizes are not universal.
- Preserve existing local files and migrate by copying.
- Treat CLI code as a useful implementation reference, not a certified Windows backend.
- Keep a simulated device available for automated development; maintain a separate physical-hardware test gate.

## Current acceptance work

Milestones 0–2 supplied the working live-color prototype. Main implementations from milestones 3–5 are in the editor, with 120 simulated checks, ten UI checks, and accepted temporary profile/effect device commands. Complete physical tests of remapping, Fn macros, recording, and onboard persistence before calling those hardware-certified. Full onboard readback and atomic rollback remain unestablished. Grouped/game/music integrations and firmware updating remain separate investigations.

No firm completion dates are assigned until the device handshake and first physical lighting test pass; those determine the remaining protocol uncertainty.


