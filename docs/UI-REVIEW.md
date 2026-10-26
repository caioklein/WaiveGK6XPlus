# UI and label review

The original Live/Onboard radio buttons represented the device protocol modes. That made the meaning of Apply depend on a separate setting, while Save referred only to the PC. Users had to understand implementation details to know where changes would go.

The revised interface names the action and destination directly:

| Control | Behavior |
| --- | --- |
| Apply now | Try the edited profile through the app; the app stays running, including in the tray. |
| Save lighting onboard | Store lighting in keyboard memory and preserve existing key assignments and macros. |
| Save on PC | Keep an editable profile bundle on the PC. |
| Save full profile onboard | In Key mapping, store lighting plus configured key assignments and referenced macros. |
| Use onboard settings | Stop app control and switch to the selected keyboard memory slot. |

The memory selector has one stable meaning: the destination for onboard saves and the slot to return to when app control stops. It no longer changes the Apply action. Built-in lighting supports lighting only; editable Layers 1–3 also support key assignments and key-press effects. The labels use the keyboard's visible layer numbers rather than internal firmware mode numbers.

Removed the redundant header minimize button. The Windows minimize button still hides to the system tray. The footer states how Minimize and Close behave.

Replaced Profile look with Custom lighting effect, Libraries with Macros & custom effects, Copy with Duplicate, Delete with Delete saved copy, and technical USB status with the current source of keyboard settings. Status messages now name the same controls the user sees. Error messages explain which memory slots support Fn shortcuts and key-press glow.

Keyboard features and protocol operations are retained. The action-specific copies prevent a lighting-only save from accidentally writing remaps. Verification compiles these plans without sending writes to the keyboard, alongside the existing protocol checks and UI smoke checks.
