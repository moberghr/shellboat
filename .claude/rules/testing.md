# Testing Rules (§4.x)

- **xUnit v2** only, `Xunit.SkippableFact` for live suites, fakes in each project's `Fakes.cs`. No Moq,
  no NSubstitute, no `xunit.v3.*` packages.
- **A skip is not a pass.** Live suites: `Shellboat.Ssh.Tests` (start `./build/test-sshd.sh`), the RDP
  native smoke test (build `./build/native.sh`). Run them before reporting a protocol change as working.
- Assert observable behaviour (bytes on the wire, cells in a snapshot, saved documents), not "does not throw".
- UI: never claim visual correctness. Pure helpers get unit tests; rendering is the user's eyeball QA.
- When vendored code is patched, add a test that pins the reason (e.g. `Wide_characters_occupy_two_cells`).
