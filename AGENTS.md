# AGENTS.md

> Portable guidance for AI coding tools (Codex, Cursor, Gemini CLI, …). Claude Code reads `CLAUDE.md` +
> `.claude/rules/` instead; this mirrors their essentials. **Source of truth:** `CLAUDE.md`,
> `.claude/rules/*.md`, `.claude/references/*`.

## What this is

Shellboat is a cross-platform **Avalonia 12.1 desktop** connection manager (.NET 10): a folder tree of saved
connections on the left, **SSH terminals** and **RDP desktops** in tabs on the right. Same stack as Bearing.

```
Shellboat.Core          abstractions + records only — zero dependencies
  ↑        ↑        ↑         ↑
Terminal  Ssh      Rdp    Persistence     (vendored XtermSharp · SSH.NET · FreeRDP wrapper P/Invoke · JSON/keychain/keepassxc-cli)
  ↑        ↑        ↑         ↑
Shellboat.App           Avalonia MVVM shell
  ↑
Shellboat.Desktop       thin entry point
```

- **UI:** Avalonia + CommunityToolkit.Mvvm; manual composition root in `src/Shellboat.App/App.axaml.cs`.
- **Vendored:** `vendor/XtermSharp` (terminal engine), `native/freerdp-wrapper` (from RDPilot). Each has a
  `NOTICE.md` listing local changes.

## Build / test

- Build: `dotnet build` · Test: `dotnet test` · Format: `dotnet format --verbosity quiet`
- Live SSH tests: `./build/test-sshd.sh` first. RDP native: `./build/native.sh` first. A skip is not a pass.

## Critical rules

1. **NEVER add a reference to `Shellboat.Core`.**
2. **NEVER claim UI/visual behaviour is verified** — say what tests asserted; visuals need human QA.
3. **NEVER log, persist or pass on a command line any password/passphrase/master password** outside
   `ISecretStore`; no on-disk fallback when there is no keychain.
4. **Host trust is never silent:** unknown SSH key → prompt; changed key → hard block.
5. **No protocol/credential/file logic in Views, Controls or code-behind.**
6. **Vendored code changes are recorded in that folder's `NOTICE.md`.**

## Conventions

- File-scoped namespaces, `var`, early return, **braces on all control flow**. Full guide:
  `.claude/references/dotnet/coding-guidelines.md`.
- xUnit v2 only, hand-rolled fakes, `SkippableFact` for live suites.
- Commits are opt-in; short lowercase imperative messages.
