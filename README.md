# Shellboat

A cross-platform connection manager in the spirit of Ásbrú and MobaXterm: a folder tree of saved connections
on the left, **SSH terminals** and **RDP desktops** in tabs on the right. It runs on macOS, Windows and Linux
(.NET 10 + Avalonia).

## What works (v1)

- Connection tree with folders and subfolders. You can add, edit, rename, duplicate and delete, drag and drop
  into folders, filter, and double-click (or press Enter) to connect.
- **SSH** in embedded terminal tabs (xterm-256color). Supports password or private-key auth (with passphrase),
  resizing, scrollback (wheel, Shift+PgUp/PgDn), copy-on-select, right-click paste and bracketed paste.
  An unknown host key asks before trusting it; a changed host key blocks the connection.
- **SFTP file browser** for SSH connections (right-click → *Open SFTP (files)*): browse folders and download
  files or whole folders. Double-click a file to save it to `~/Downloads`; use *Download to…* to pick a folder.
  Existing files are never overwritten (`name (1).ext`). Right-click a row to download, copy its path, rename it
  or see its properties (size, dates, permissions, owner).
- **RDP** in embedded tabs via FreeRDP 3. The remote resolution follows the tab size, the remote cursor is
  shown, text clipboard works in both directions, and certificates prompt on first use or when they change.
- Secrets are stored in the **OS keychain** (macOS Keychain, Windows Credential Manager, libsecret). Nothing
  secret is written to `connections.json`.
- **Import from Royal TSX / Royal TS** (`.rtsz`): folders, SSH and RDP connections, hosts, ports, users and key
  paths. Royal's saved passwords are encrypted with an undocumented scheme and are **not** imported; you're asked
  on first connect, or you can import them from KeePass.
- **Import from Ásbrú Connection Manager** (`asbru_export.yml`): groups, SSH and RDP connections, users,
  key files and passwords (into the keychain). Delete the export afterwards — it holds passwords in plain text.
- **Import from KeePass**: reads any `.kdbx` through `keepassxc-cli`. KeePass groups become folders and
  passwords go into the keychain.

## Build & run

```sh
dotnet build
./build/native.sh            # RDP engine: needs FreeRDP 3 (macOS: brew install cmake pkgconf freerdp)
SHELLBOAT_PROFILE=dev dotnet run --project src/Shellboat.Desktop
SHELLBOAT_PROFILE=dev ./build/macos-app.sh --run   # macOS: as dist/Shellboat.app, with the Dock icon
```

SSH works without the native build. Without it, an RDP connect reports that the engine is missing.

## Tests

```sh
./build/test-sshd.sh         # throwaway OpenSSH container on 127.0.0.1:2222 for the live SSH tests
dotnet test
./build/test-sshd.sh stop
```

## Credits

- Terminal engine: [XtermSharp](https://github.com/migueldeicaza/XtermSharp) (MIT), vendored in `vendor/XtermSharp`.
- RDP wrapper and viewport logic: [RDPilot](https://github.com/ErnieBernie10/RDPilot) (MIT), vendored in
  `native/freerdp-wrapper` and ported in `src/Shellboat.Rdp` / `src/Shellboat.App/Rdp`.
- [FreeRDP](https://www.freerdp.com/) (Apache-2.0) and [SSH.NET](https://github.com/sshnet/SSH.NET) (MIT).

## License

MIT — see [LICENSE](LICENSE). Third-party components and their licenses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
