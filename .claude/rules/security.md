# Security Rules (§1.x)

## §1.1 — Secrets
- Only `MacKeychainSecretStore`, `WindowsCredentialSecretStore`, `SecretToolSecretStore` store anything.
  Keyed by connection id + `SecretPurpose` (password / key passphrase).
- No keychain → `NoSecretStore`: writes throw `SecretStorageRefusedException`, reads return null. **No
  on-disk fallback, and do not add one.** The status bar shows the reason (`UnavailableReason`), never a
  guessed cause.
- `connections.json` and `known_hosts.json` hold no secrets; written atomically, mode 0600 on Unix.
- Never put a secret in an exception message, `ToString()`, log line or process argument. `CliResult.Detail`
  is stderr only, because stdout can be the secret.

## §1.2 — Host trust
- SSH: `JsonTrustStore` (own known_hosts). Unknown → `ITrustPrompter`; **Changed → hard block**, the user
  removes the entry deliberately. Do not add an "accept anyway" button.
- RDP: FreeRDP's own certificate store decides first; the wrapper asks us only for unknown/changed certs,
  and the dialog shows the previous fingerprint on a change.

## §1.3 — KeePass import
- Through `keepassxc-cli` only; master password on stdin, cleared from the view model after use.
- Exported XML stays in memory. Imported passwords go to the keychain; with no keychain they are dropped
  and the user is told.

## §1.4 — Terminal input
- Bracketed paste strips any embedded end marker so a paste cannot smuggle keystrokes.
- The emulator reports `IsProcessTrusted() == false`: a remote host cannot read our buffer back.

## §1.5 — Royal TS/TSX import
- `RoyalTsImporter` reads the `.rtsz` XML for structure only. Royal's encrypted password fields are counted
  and reported, **never decrypted** — do not add reverse-engineering of Royal's encryption.
- Royal object ids are reused, so re-importing the same document updates connections instead of duplicating.
