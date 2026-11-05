# Architecture Rules (§2.x)

## §2.1 — `Shellboat.Core` is dependency-free
Records (`ConnectionEntry`, `Folder`, `ConnectionTreeDocument`, …) and interfaces (`IConnectionStore`,
`ISecretStore`, `ITrustStore`, `ITrustPrompter`, `IShellChannel`, `ICredentialImporter`). No references, ever.

## §2.2 — Dependency direction
`Core ← Terminal, Ssh, Rdp, Persistence ← App ← Desktop`. The four middle projects know nothing of each
other or of Avalonia. Only `App` composes them.

## §2.3 — MVVM
- View models own behaviour (`MainWindowViewModel`, `ConnectionTreeViewModel`, `TerminalTabViewModel`,
  `RdpTabViewModel`, `ConnectionEditorViewModel`, `KeePassImportViewModel`).
- Views/Controls render and forward input. `TerminalControl` turns keys into bytes via `TerminalKeyEncoder`
  and asks the emulator for snapshots — it never talks to SSH.
- Everything the UI asks the user goes through `IDialogService` (fake in tests).

## §2.4 — Composition root
`App.axaml.cs` wires stores, dialogs, the SSH channel factory and the importer by hand. No container.

## §2.5 — Seams for tests
`IShellChannel` (SSH), `INativeRdpSession` (FreeRDP), `ICliRunner` (keychain + keepassxc-cli). Add a seam
rather than reaching for a mocking library.

## §2.6 — No OS drag-and-drop for in-app moves
`DragDrop.DoDragDropAsync` with only an in-process `DataFormat` aborts the process on macOS (AppKit's
`NSDraggingSession` throws an Objective-C exception .NET can't catch — crash 2026-11-05). Moves inside the app use
pointer capture (`Controls/TreeDragController`). Use OS drag-and-drop only when data really leaves the app, and then
always include a platform format (text/file).
