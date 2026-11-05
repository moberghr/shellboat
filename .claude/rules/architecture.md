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

## §2.7 — Settings, shortcuts, menu
`AppSettings` (Core) ↔ `JsonSettingsStore` (settings.json, no secrets) ↔ `SettingsService` (App, live; raises
`Current`/`Shortcuts` changes). Shortcuts: `Settings/ShortcutCatalog` (action ids are persisted — never rename;
Windows/Linux defaults are Ctrl+Shift chords so plain Ctrl+letter reaches the shell) → `ShortcutMap`. Every key
shortcut and menu item goes through `MainWindowViewModel.RunActionAsync(id, tab, source)`; windows catch keys in a
**tunnel** handler so the focused terminal never sees them. The menu (`Views/MainMenu`) is a `NativeMenu` **built once per window and
updated in place** (`Refresh`) — calling `NativeMenu.SetMenu` again with a new menu on a window whose menu is
already exported throws in Avalonia.Native ("The menu being updated does not match") and aborts the app (crash
2026-11-05). The macOS app menu must be set in `App.Initialize`, before any window; no top-level "Edit" menu (AppKit would claim ⌘C/⌘V). Colours: theme tokens live in
`Tokens.axaml` ThemeDictionaries (keep Light/Dark key sets identical); the terminal's in `Terminal/TerminalTheme`.
