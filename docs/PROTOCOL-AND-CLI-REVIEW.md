# Protocol and gk6ctl review

Date: 23 October 2026. Findings below are from source inspection and offline comparison, not new physical keyboard tests.

## Evidence sources

| Functionality | Recovered app source | CLI reference |
| --- | --- | --- |
| Commands and CRC | src/libs/cmd.js; src/libs/utils.js | lib/proto.js; lib/crc16.js |
| Discovery and requests | src/drivers/cor-m3-mk.js; src/libs/device.js; src/libs/driver.js | lib/transport.js |
| Profile compilation | src/libs/profile.js | lib/flash.js |
| Packed file codec | src/libs/cmfcodec.js | lib/cmf.js |
| Macro text/encoding | src/libs/macro.js; src/libs/keymap.js | lib/macrofmt.js; lib/keys.js; lib/spec.js |
| Live effects | src/libs/cmleplayer.js; src/libs/cmlemanager.js | Not implemented |
| Live macro playback | src/libs/scriptengine.js; src/libs/scripteginemanager.js | Not implemented |
| Model/layout data | resources/device | Expects external data/files |

Recovered paths are relative to recovered-app. CLI paths are relative to reference/gk6ctl.

The original Electron archive contains readable backend JavaScript and a compiled frontend. Its native Node modules provide generic HID, USB notification, and input-hook functionality. No device-specific native binary has yet been identified as necessary to decompile.

## Packet framing recovered from source

Original driver metadata: vendor 0x1EA7, keyboard product 0x0907, vendor collection usage page 0xFF00 / usage 0x0050. Bootloader metadata uses product 0x0905. Those are reference hints; enumerate and confirm descriptors before opening a collection. The original driver probe matches VID/PID without enforcing the declared usage fields.

A protocol report is 64 bytes:
- byte 0: command.
- byte 1: subcommand/mode.
- bytes 2–5: little-endian header.
- bytes 6–7: little-endian CRC16.
- bytes 8–63: up to 56 payload bytes.

The encoder starts with zeroes, fills fields, and calculates CRC across the 64-byte report while the CRC field is still zero. CRC uses initial 0xFFFF and polynomial/table 0x1021. Windows report-ID prefix handling belongs to the transport and must match the actual collection's report capabilities; original intent is a preceding zero report ID.

Important commands:
- 0x01 / 1,2,8,9: firmware, device ID, model ID, matrix.
- 0x0B: mode selection; 0x0C: keepalive.
- 0x15: key reporting / key and mouse events.
- 0x16: live key tables; 0x17: live lighting configuration.
- 0x1A: live RGB frames and commit.
- 0x21: clean a profile data region.
- 0x22 / 0x31: base / Fn key assignments.
- 0x24: stored static lighting.
- 0x25: stored macro blocks.
- 0x26 / 0x27: key-effect indices / stored effect data.

Address and length packing differs by command: several use length shifted 16 bits, while lighting data/frame commands use a 24-bit shift. Do not generalize into one header formula without fixtures.

Profile ModeIndex 0 is host-controlled; the keyboard's live operating mode is 5. Those are different namespaces. The CLI permits onboard profile indices 1–4. Actual visible slots and restrictions must come from the target model and testing.

The original source mixes a 132-key live matrix, 176 lighting slots, 128 key slots per macro index region, 512-byte macro blocks, and a 128 KiB lighting buffer budget. Preserve the observed encodings but establish per-model capacities before allocating or writing.

## File compatibility

The packed codec uses a 32-byte header, a type/name-dependent CRC/XOR encoding, and body checksums. Profile bodies are JSON; macro bodies use an INI-like event format; lighting bodies contain frames and color configurations. This is file encoding, not a security boundary.

GPR exports are ZIP-based profile archives. Saved profile files can reference separately stored macro and lighting files, so migration must collect dependencies and preserve GUIDs. Existing local profile backup is not a hardware flash dump.

## What gk6ctl contributes

Repository: [tfsoares/gk6ctl](https://github.com/tfsoares/gk6ctl).
Reviewed commit: 6d026860c47bdbda47983593697c1a383d111916.
License: MIT; retain its copyright and permission notice when adapting code.

Its separated protocol, codec, key-spec builder, and onboard compiler are useful porting references. Prefer porting validated algorithms into a shared C# core over shipping a Node sidecar permanently. The unmodified CLI remains a reference tool; no dependencies were installed and no CLI hardware write was run.

Offline comparison:
- 11 shared command examples match the unmodified original packet bytes.
- Synthetic profile, light, and macro files encode identically and cross-decode correctly.
- Raw fixtures are in reference/protocol-vectors.json; comparison results in reference/gk6ctl-comparison.json.
- Reproduce with node tools/export-protocol-reference.cjs and node tools/compare-gk6ctl.cjs.

These checks establish the tested encodings, not complete compiler equivalence or hardware support.

## Limitations found in the CLI

- Its README documents hardware verification on macOS M1 with a COR-M3 75 model (656802006), not the cached 96XS-RGB model or this Windows installation.
- Its transport writes the raw 64-byte report; Windows report-ID handling is not implemented there.
- Discovery probes all matching keyboard paths instead of strictly selecting the declared vendor collection.
- Requests do not serialize concurrent callers. A native GUI needs a real device queue and independent notification routing.
- There is no live-mode lighting engine, visual editor, or macro recorder.
- Fn macro references reuse the normal index region; the writer reads at 256 + index from a 256-byte buffer. This repeats the original bug and needs an explicitly tested corrected mapping.
- Profile writes await replies but do not inspect their ACK results.
- The macro key map is a subset and maps unrecognized names to zero.
- Several capacities are hardcoded; no capability negotiation is established.
- The packed decoder does not validate file magic, declared size, or checksums before accepting a body.
- The backup implementation uses tar and shell-oriented conventions; it is a local-data backup.
- A supported-looking VID/PID does not prove every device model or feature works.

## Unresolved hardware questions

Confirm actual connected model and firmware, HID report sizes, correct Windows framing, supported onboard slots, static versus effect-lighting routing, per-command ACK semantics, accepted write pacing, and persistence after power loss.

No confirmed readback mechanism for complete current onboard profiles has been established by this review. Define Apply success as device acknowledgement unless later evidence supports stronger verification.

The recovered firmware.js is empty and GetGameList returns an empty array. The CLI's use of “flash” means onboard configuration writes, not keyboard firmware replacement.

## Why the native design is different

Use compiled typed values and model-aware validation instead of copying unchecked JavaScript buffers. Separate local saving, persistent application, and temporary live previews. Make the first acceptance test a visible static-color change on the confirmed keyboard, then build broader functionality on that verified transport.
