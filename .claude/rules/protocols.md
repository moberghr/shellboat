# Protocol Notes (§9.x)

## §9.1 — Terminal
- `TerminalEmulator` is the only door into XtermSharp; all access under one lock. The UI renders immutable
  `TerminalSnapshot`s and coalesces repaints.
- ConvertEol is off (remote PTYs send CRLF). TERM is `xterm-256color`.
- Known gaps (v1): no 24-bit colour (engine maps to 256), no mouse reporting to apps, no combining marks
  (rendered as separate cells), no ligatures.

## §9.2 — SSH
- `SshShellChannel`: password + keyboard-interactive for password auth, `PrivateKeyFile` for keys.
  `AuthenticationFailedException` = re-prompt; `HostKeyRejectedException` = never retry.
- Not yet: ssh-agent, jump hosts, port forwarding, SFTP browser.

## §9.3 — RDP
- Ported from RDPilot (commit in `native/freerdp-wrapper/NOTICE.md`). Frames: native thread signals, UI
  thread copies dirty rects (`ManagedFramePresenter`). Cursor arrives out of band (`RemoteCursorCache`).
- Dynamic resolution via the display channel, debounced (`ViewportResolutionUpdateScheduler`).
- Not yet: gateway, keyboard grab, file clipboard, non-US keyboard layouts on macOS/Linux, bundling FreeRDP
  for distribution, Windows native build script.
