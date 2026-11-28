# Dock icons

Illustrated tiles for the Dock's own buttons, drawn as SVG and packed into one atlas that `build.cmd` embeds:

| File | Cells | Used by |
|---|---|---|
| `src/DockIcons.png` | 5 × 1, 128 px | Dock system slots: `start`, `taskview`, `work`, `suruh`, `shelf` (empty Rak) |

Cell order is `IDS` in `make_icons.py`, mirrored by `src/DockArt.cs`.

## Style

Same language as the Settings art (`assets/settings`, helpers imported from `make_art.py`): a gradient squircle with a soft sheen and hairline rim, one bold ivory motif, at most one calm accent. Dock icons are drawn at about 24–40 px, so each motif is two or three heavy shapes and nothing thinner than 8 units of the 128 box.

| Button | Tile | Motif |
|---|---|---|
| Start | brand plum | launcher grid, last cell the Deskmate mint pill |
| Task view | slate | two stacked windows |
| Work | amber | clipboard with a mint tick |
| Suruh | violet | chat bubble with a sparkle |
| Rak | emerald | tray holding a sheet |

## Rebuild

Edit `make_icons.py`, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File assets\dock\render_icons.ps1
```

It writes the SVGs to `svg/` and a dark/light `preview.html` (both ignored by git), rasterises the atlas with headless Edge on a transparent background and recompresses it with Pillow. Rebuild `Deskmate.exe` afterwards.
