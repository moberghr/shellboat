# Vendored: XtermSharp

VT100/xterm emulation engine from https://github.com/migueldeicaza/XtermSharp (`XtermSharp/`), MIT licensed
(see `LICENSE`; derived from xterm.js).

- Upstream commit: `1bed529cd20748ef9da43753e8dd553f65fc5582`
- Not copied: `Pty.cs` (local forkpty — Shellboat's PTY is the remote SSH channel), `XtermSharp.Mac`, UI hosts.
- Local changes (record every one here):
  - `InputHandlers/InputHandler.cs`, `SelectionService.cs`: added `using Rune = System.Rune;` — on .NET Core+
    `System.Text.Rune` exists too and the bare name is ambiguous. Upstream targets netstandard2.0.

Compiled into `src/Shellboat.Terminal` via `<Compile Include>`; `.editorconfig` marks it generated so
`dotnet format` leaves it alone.
  - `InputHandlers/InputHandler.cs` (Print): wide characters get width 2 via `Shellboat.Terminal.WideChars`
    (upstream hard-codes 1; its own `RuneHelper.ConsoleWidth` table lookup reads out of bounds). Combining marks deliberately stay width 1 — upstream's width-0 path adds the
    codepoints together, corrupting the previous cell.
  - `TerminalOptions.cs`: `Scrollback` gets a setter (upstream: get-only, fixed at 1000 in the constructor) so the
    scrollback length is a user setting. Pinned by `Scrollback_length_follows_the_option`.
  - `Terminal.cs` (SaveCursor): also saves margin/origin/wraparound modes. Upstream's `RestoreCursor` restored them
    from fields nothing ever wrote, so the first ESC 8 / `CSI u` (shell prompts send these) turned auto-wrap off and
    long lines ran off the right edge. Pinned by `Long_lines_still_wrap_after_save_and_restore_cursor`.
