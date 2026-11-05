# Shellboat — Engineering Standards

> Cross-platform connection manager (Ásbrú/MobaXterm-style): a folder tree of saved connections on the left,
> SSH terminals and RDP desktops in tabs on the right. Same stack and conventions as Bearing
> (`~/Development/bearing`). This file + `.claude/rules/` are the source of truth for AI agents; `AGENTS.md`
> mirrors the essentials for other tools.

## Tech Stack

- **.NET 10**, `Nullable` on, implicit usings, central package versions (`Directory.Packages.props`), MinVer.
- **UI:** Avalonia 12.1 + Fluent theme, MVVM via CommunityToolkit.Mvvm. Manual composition root in
  `src/Shellboat.App/App.axaml.cs` (no DI container).
- **SSH:** SSH.NET (`Shellboat.Ssh`). **Terminal emulation:** vendored XtermSharp (`vendor/XtermSharp`, MIT),
  compiled into `Shellboat.Terminal`, rendered by our own `Controls/TerminalControl`.
- **RDP:** FreeRDP 3 through a C wrapper vendored from RDPilot (`native/freerdp-wrapper`, MIT), P/Invoke in
  `Shellboat.Rdp`, viewport in `Shellboat.App/Rdp` + `Controls/RdpViewportView`.
- **Secrets:** OS keychain only (macOS `security`, Windows Credential Manager, libsecret `secret-tool`).
  **KeePass import:** `keepassxc-cli export --format xml` — no KDBX crypto of ours, no GPL dependency.
- **Tests:** xUnit v2 + `Xunit.SkippableFact`, hand-rolled fakes (`tests/**/Fakes.cs`), no mocking library.

## Commands

- Build: `dotnet build`
- Test: `dotnet test` — the live SSH suite needs `./build/test-sshd.sh` (docker); the RDP native smoke test
  needs `./build/native.sh`. **Run both before trusting a green run — a skip is not a pass.**
- Native RDP wrapper: `./build/native.sh` (macOS: `brew install cmake pkgconf freerdp`; Linux: FreeRDP 3 dev
  packages). Output lands in `native/out/<rid>/` and is copied next to the app by `Shellboat.Rdp.csproj`.
- Run: `SHELLBOAT_PROFILE=dev dotnet run --project src/Shellboat.Desktop` (isolated config + keychain namespace).
  On macOS a bare `dotnet run` gets the generic "exec" Dock icon; `./build/macos-app.sh --run` runs it as
  `dist/Shellboat.app` (Info.plist + `assets/brand/icons/shellboat.icns`). `--release` = self-contained publish.
- Format: `dotnet format --verbosity quiet`

## Layout

```
Shellboat.Core          records + interfaces only — ZERO references
  ↑        ↑        ↑         ↑
Terminal  Ssh      Rdp    Persistence     (xterm engine · SSH.NET · FreeRDP P/Invoke · JSON/keychain/KeePass)
  ↑        ↑        ↑         ↑
Shellboat.App           Avalonia MVVM shell (ViewModels/Views/Controls/Connections/Rdp/Terminal/Services)
  ↑
Shellboat.Desktop       thin entry point (exe: `shellboat`)
```

## Critical Rules (always apply)

- **§0.1 NEVER add a package or project reference to `Shellboat.Core`.**
- **§0.2 NEVER claim UI/visual behaviour is verified.** Build + tests are the automated ceiling; rendering,
  keyboard feel, RDP visuals need the user's eyeball QA. Say what was asserted.
- **§0.3 NEVER log, print, persist or put on a command line a password, passphrase or master password**
  outside `ISecretStore`. Secrets go to helpers on **stdin** (exception: macOS `security -w`, documented).
  Records holding secrets override `ToString()`. No on-disk fallback when no keychain exists — prompt and keep
  in memory (`NoSecretStore`).
- **§0.4 Host trust is never silent.** Unknown SSH host key → prompt; **changed key → hard block** (no
  click-through). RDP certificates go through FreeRDP's store + the user prompt. Never auto-accept.
- **§0.5 No protocol, credential or file I/O logic in Views/code-behind/Controls.** It belongs in a view model,
  `Services/`, or the lower layers. Code-behind wires events and builds visuals.
- **§0.6 Vendored code (`vendor/`, `native/freerdp-wrapper/`) is upstream's.** Change it only when necessary
  and record every change in that folder's `NOTICE.md` with the reason.
- **§0.7 Don't grow god objects.** `MainWindowViewModel` coordinates; tree rules live in
  `Connections/ConnectionTreeEditor` (pure), session logic in the tab view models.

## Conventions

- File-scoped namespaces, `var` for locals, early return, **braces on every control-flow body**. Full guide:
  `.claude/references/dotnet/coding-guidelines.md`.
- Pure logic goes in stateless helpers so it is testable without a UI (`ConnectionTreeEditor`,
  `KeePassImportMapper`, `TerminalKeyEncoder`, `AvaloniaKeyMap`, RDP input mappers).
- Propagate `CancellationToken` through async I/O.
- Commits are opt-in; short lowercase imperative messages.

## Rules index

| File | Covers |
|---|---|
| `.claude/rules/architecture.md` | layers, MVVM, composition root |
| `.claude/rules/security.md` | secrets, trust, KeePass import |
| `.claude/rules/testing.md` | xUnit, live suites, skip ≠ pass |
| `.claude/rules/protocols.md` | terminal, SSH, RDP specifics and known gaps |
