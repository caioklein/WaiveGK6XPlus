# Native Apply fix — version 0.1.1

The native app's response parser read the echoed subcommand as the success status. The 96XS-RGB firmware 4.5 replies with command, subcommand, status for the tested key-table and lighting commands.

Captured examples:
- 16 03 01: key-effect index table command succeeded.
- 1A 02 01: final live-lighting commit succeeded.

Version 0.1.0 incorrectly rejected both because byte 1 was not 01. That aborted Apply and returned to built-in mode, producing the reported flash/reconnect cycle. The request-packet fixtures were correct; the original synthetic responses had the wrong shape and failed to expose this parser bug.

Changes:
- Check success at byte 2 and verify command/subcommand correlation.
- Ignore notifications and replies for other subcommands.
- Preserve the connection after an explicit rejection when onboard-mode recovery succeeds.
- Keep reconnect requirements for failed/timed-out sessions.
- Report partial stored writes only for stored operations.
- Record bounded, thread-safe command traces in local diagnostics. Input key notifications are ignored.
- Add captured hardware response fixtures and negative/truncated/wrong-subcommand regression cases.

Validation:
- 61 protocol/controller checks passed.
- A traced temporary-color application succeeded on the connected 96XS-RGB, firmware 4.5, including the key tables, ten RGB chunks, and final commit.
- Restoring built-in mode succeeded.
- The test did not write onboard configuration or firmware.
- The test confirms accepted device commands; physical appearance and long-running live behavior still need observation in the app.

Evidence: native/artifacts/apply-trace-before.json, apply-trace-after.json, and apply-trace-verified.json. Regression inputs are saved in reference/native-ack-replies.json.
