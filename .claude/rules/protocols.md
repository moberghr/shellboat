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
- Shared login: `SshLogin` (auth methods, host-key check, exception mapping) backs both `SshShellChannel` and
  `SshFileChannel`; the App side shares the credential loop in `Services/SshConnectFlow`.
- SFTP browser (`SftpTabViewModel`, "Open SFTP" on SSH rows): browse, download (files and folders, recursive),
  and a row menu with copy path, rename (same folder only; `RenameAsync` refuses an existing target) and properties. Remote names are server input — `LocalDownloads.SafeName`/`UniquePath` keep them inside the
  target folder and never overwrite; symlinked folders are not followed; a failed file is deleted, not left partial.
- SFTP upload (toolbar "Upload…" / "Upload folder…", or drop files/folders from the OS file manager onto the list) goes
  into the folder being shown (`SftpUploader`). Never overwrites: a taken name becomes "name (1).ext", and
  `SshFileChannel.UploadAsync` reserves the file with an exclusive create (`FileMode.CreateNew`) before copying, so a
  failed/cancelled upload deletes only its own partial file. Symlinked local folders are not followed.
- Not yet: ssh-agent, jump hosts, port forwarding, SFTP delete/move between folders/replace-on-upload.

## §9.3 — RDP
- Ported from RDPilot (commit in `native/freerdp-wrapper/NOTICE.md`). Frames: native thread signals, UI
  thread copies dirty rects (`ManagedFramePresenter`). Cursor arrives out of band (`RemoteCursorCache`).
- Dynamic resolution via the display channel, debounced (`ViewportResolutionUpdateScheduler`).
- Not yet: gateway, keyboard grab, file clipboard, non-US keyboard layouts on macOS/Linux, bundling FreeRDP
  for distribution, Windows native build script.
