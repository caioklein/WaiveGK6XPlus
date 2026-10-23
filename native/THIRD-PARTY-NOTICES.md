# Third-party references and data

The keyboard layout and standard live key table in Data originate from the recovered GK6+ 8.0.7 application by DONGGUAN JIZHIDIANZI Co., LTD. The original package declares the MIT license.

The protocol implementation was written in C# from the recovered source and checked against original packet fixtures. The original JavaScript source is preserved in reference/legacy-electron-source.zip in this workspace.

gk6ctl by Tiago Soares was used as a reviewed reference and compatibility comparator. Its source was not bundled into the native application. The upstream MIT license is retained in reference/gk6ctl/LICENSE. Reviewed revision: 6d026860c47bdbda47983593697c1a383d111916.

No Electron, Chromium, Node.js, node-hid, or gk6ctl runtime is included in this native package. The application uses Microsoft's installed .NET Windows Desktop runtime and Windows HID APIs.

The upstream license is included as GK6CTL-LICENSE.txt. The C# profile, macro, lighting, and packed-file implementations were checked against the recovered JavaScript and the reviewed CLI reference.

