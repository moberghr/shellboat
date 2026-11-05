# Shellboat — brand assets

A **sailboat carrying a shell prompt**: steel mainsail, teal jib, and a `>_` cut into the hull. It sits on
Bearing's graphite tile with Bearing's teal (`#35D0BE`) and steel (`#D8DEE6`), so the two tools look like a set.

## Source SVGs (100×100 mark, scaled ×1.52 onto the 256 tile — same geometry as Bearing's tile)
| File | Use |
|------|-----|
| `shellboat-tile.svg` | App-icon master for raster sizes >24px: mark + waterline on the graphite tile. |
| `shellboat-tile-small.svg` | ≤24px master: no waterline, solid sails, heavier prompt. |
| `shellboat-mark.svg` | The mark alone, for in-app use on dark backgrounds. |

## Generated (`icons/`, committed)
Rebuild with `./build-icons.sh` (ImageMagick if installed, otherwise headless Chrome; plus python3):

- `shellboat.ico` — Windows exe icon (`ApplicationIcon` in `Shellboat.Desktop.csproj`), PNG entries 16–256.
- `shellboat.icns` — macOS, for an `.app` bundle (not built yet).
- `png/` — the size ramp. `tile-256.png` is copied to `src/Shellboat.App/Assets/shellboat-256.png`, the window icon.

`pack-icns.py` is Bearing's; `pack-ico.py` packs the `.ico` without needing icoutils.
