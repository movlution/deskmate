# Settings artwork

Illustrated category art for the Settings window, drawn as SVG and packed into two atlases that `build.cmd` embeds:

| File | Cells | Used by |
|---|---|---|
| `src/SettingsIcons.png` | 7 × 2, 128 px | category tiles: hub cards, page hero, breadcrumb, side rail, prev/next footer |
| `src/SettingsScenes.png` | 2 × 7, 400 × 160 px | banner scenes on hub hero/wide cards and on every page hero |

Cell order is `IDS` in `make_art.py`, mirrored by `src/SettingsArt.cs`. The Deskmate (about) page keeps the app icon.

## Style

- Tile: squircle (superellipse n 4.6) in the category's paper twin from `Ink.Labels`, nudged in hue per category so two pages sharing a label still differ. Soft top-left sheen, a hairline rim, no outer glow.
- Motif: one bold ivory (`#FBF9F5`) shape that still reads at 20 px, a ghosted ivory layer for depth, and at most one or two calm accent spots (mint, gold, rose, sky). Details on ivory use the tile's deep tone. A short coloured drop shadow lifts the motif.
- Scene: a tilted, enlarged copy of the tile with a small ghost tile low on its left, one faint ring, a soft colour bloom and two small dots. The top-right corner stays empty so the status pill and palette dots sit on a plain surface. A mask fades it to nothing over the left third, so card text always sits on a plain surface; `SettingsArt.SceneReserve` keeps text out of the art.

## Rebuild

Edit `make_art.py`, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File assets\settings\render_art.ps1
```

It writes the SVGs to `svg/` and a side-by-side dark/light `preview.html` (both ignored by git), rasterises the atlases with headless Edge on a transparent background and recompresses them with Pillow. Rebuild `Deskmate.exe` afterwards.
