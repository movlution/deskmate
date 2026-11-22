# Deskmate design system

Deskmate sits on your desktop all day. It should feel quiet: soft squircle shapes, tinted neutrals, and one calm accent. Nothing glows, nothing is neon, and no surface is a flat Windows gray. The brand comes from the icon: deep plum, ivory, and mint. The UI turns these into a violet-charcoal night, ivory paper, a dusty lavender accent, and mint for positive states.

Mode: **Operate**. Scanability and native expectations come before expression. The brand shows in small details: the tint of the canvas, the accent, the icon tiles in settings rows, and soft depth on thumbs and knobs.

Source of truth: `src/Ink.cs` (`Tone`, accent presets, radii, type), `src/Kit.cs` (`KitInk` window surfaces and shared drawing helpers). Theme packs (`theme.ini`, see `docs/paket-tema.md`) override the same keys; nothing here renames or removes a pack key.

## Palette

### Canvas and ink (`Tone`)

| Token | Night (dark) | Paper (light) | Notes |
|---|---|---|---|
| `Bg` | `#1C1A22` | `#F5F2EC` | violet charcoal / warm ivory paper; also the glass veil |
| `Text` | `#F5F2EC` | `#211C2A` | ivory / plum ink. On dark glass, `OnGlass()` lifts it 60 % toward white |
| `Sub` | `Text` @ 158 α | `Text` @ 170 α | `OnGlass()` raises α to ≥ 240 |
| `Faint` | `Text` @ 148 α | `Text` @ 158 α | still ≥ 4.5:1, safe for 11 px text |
| `Fill` / `FillHover` / `Line` | ivory @ 18 / 28 / 23 α | plum @ 14 / 24 / 24 α | tinted, not gray |
| `Positive` | `#80D0A8` mint | `#2B7552` deep mint | |
| `Negative` | `#F0A090` soft coral | `#AE4639` brick coral | |
| `Warning` | `#E5C070` amber | `#7F5F14` deep amber | 9.92 / 5.30 on canvas; pack key `warning` |
| `Accent` | `#AE9DEB` | `#6650B8` | default brand violet; Grafit = `Text` |
| `OnAccent` | `#17141D` | `#FBF9F5` | whichever of night/ivory wins on WCAG, pushed to ≥ 4.5:1 |

### Window surfaces (`KitInk`, opaque)

| Surface | Night | Paper | Use |
|---|---|---|---|
| `Window` | `#1C1A22` | `#F5F2EC` | canvas |
| `Side` | `#1A1820` | `#ECE9E3` | navigation rail |
| `Group` | `#2B2930` | `#FDFDFC` | cards of rows |
| `Line` | `#36333A` | `#DCD9D4` | resting control fill, hairlines, tracks |
| `Hover` | `#2F2D34` | `#E6E3DD` | row hover |
| `Raised` | `#4B494E` | `#FFFFFF` | segmented thumb |
| `Disabled` | `#5D5A5E` | `#B5B1B1` | disabled knobs, fills |
| `Knob` | `Text` | `#FFFFFF` | slider thumb |
| `Focus` | accent, or `Text` when the accent is under 3:1 on `Window` | | keyboard ring |
| `Tint(k)` | `Bg` mixed toward `Text` by k | | control states: rest `Line`, hover 0.17, pressed 0.22 |

### Accent presets (`Ink.DarkAccents` / `Ink.LightAccents`)

Each slot keeps the same name in both themes, and `Ink.Apply` swaps it to its twin. Saved settings from older versions with the saturated presets move to the matching calm slot once at startup (`Ink.UpgradeAccent`). New installs with no saved `accent` use `Ink.DefaultAccent` (Ungu). A saved choice, Grafit included, always wins. While `accent` is unset or Grafit, wallpaper sync can still take the accent from the wallpaper, but only as a dusty tone (saturation 0.28–0.5).

| Name | Night | Paper |
|---|---|---|
| Biru | `#8FB1E6` | `#3B66AD` |
| **Ungu** (default) | `#AE9DEB` | `#6650B8` |
| Merah muda | `#E59CBC` | `#A3456E` |
| Merah | `#EC9488` | `#AE4639` |
| Oranye | `#E8A877` | `#975323` |
| Kuning | `#E2C67A` | `#78601A` |
| Hijau | `#7ECDA5` | `#2C7552` |
| Grafit | text ink | text ink |

### Labels (`Ink.Labels`, `Ink.Label(key)`)

Categorical colors for tags the user picks, such as note labels. Rows are `{ key, name, night, paper }`. The key is what gets stored; the name is a `Lang` key. `Ink.Label(key)` returns the twin for the current theme, and `Color.Empty` for `""` or an unknown key. They come from the same dusty family as the accents, and every twin is ≥ 4.98:1 on `Window` and `Group`.

| Key | Name | Night | Paper |
|---|---|---|---|
| amber | Amber | `#E6BF6E` | `#86621A` |
| emerald | Zamrud | `#7ECDA5` | `#2C7552` |
| sky | Biru | `#8CC4E0` | `#2D6C8C` |
| violet | Ungu | `#AE9DEB` | `#6650B8` |
| rose | Mawar | `#E59CBC` | `#A3456E` |
| coral | Merah | `#EC9488` | `#AE4639` |
| slate | Grafit | `#ABA8BC` | `#5B5770` |

### Markers (`Ink.Markers`)

Strokes drawn **onto user images** (Redact). These are deliberately not the calm pastels: a pastel stroke disappears on a white document, and a deep twin disappears in a dark app. Markers are theme-independent, vivid but not neon. Rows are `{ name, hex }`: Merah `#E5484D`, Kuning `#F5C451`, Hijau `#3FB27F`, Biru `#3E7BDB`, Oranye `#F0883E`, Ungu `#8C6CE0`, Putih `#FFFFFF`, Hitam `#17141D`. Contrast depends on the image underneath, so these colors make no WCAG claim. On black they range from 5.1:1 to 12.9:1; on white, Merah, Biru and Ungu reach about 3.9–4.2:1, Hijau and Oranye about 2.5–2.7:1, and Kuning only 1.6:1, so it suits highlighter use.

### Scrim and sheen (`Ink.Scrim(int alpha)`, `Ink.Sheen(int alpha)`)

These replace `FromArgb(a, 0, 0, 0)` and `FromArgb(a, 255, 255, 255)`. Scrim shades content with night black `#0B0A0E` on the dark theme and plum `#211C2A` on paper. Sheen lights content with ivory `#FBF9F5` on the dark theme and white on paper. Alpha is clamped to 0–255.

### Celebration effects (`Ink.Fx*`, `Ink.FxConfetti`)

Combo and edge-light effects stay joyful but sit beside the calm palette: no pure-saturation neon. `FxSky #6EC6EA`, `FxMint #6BD69C`, `FxGold #F4CD5C`, `FxApricot #F4A361`, `FxRose #F07A98`, `FxCoral #F0807F`, `FxLavender #B595EC`, `FxCream #FBF3DE`. Each is ≥ 6.5:1 on the night canvas. `FxConfetti` runs cool to warm: sky, mint, gold, apricot, rose, lavender, cream.

## Contrast (WCAG 2.x, computed from the built binary)

Measured with `Get-DeskmateContrast` against `Deskmate.ds.exe`. Floors: text and secondary ink ≥ 4.5, tertiary ink ≥ 4.5 (11 px+), accent on surface ≥ 3, ink on accent ≥ 4.5.

| Pair | Night | Paper |
|---|---|---|
| Text / Window | 15.40 | 14.87 |
| Sub / Window · Group · Line | 8.44 · 7.04 · 6.09 | 6.70 · 7.35 · 5.32 |
| Faint / Window · Group · Line | 7.29 · 6.08 · 5.26 | 5.67 · 6.23 · 4.50 |
| Tone Sub / Bg · Faint / Bg (alpha over canvas) | 6.61 · 5.94 | 5.24 · 4.52 |
| Accent (Ungu) / Window · Group | 7.23 · 6.03 | 5.52 · 6.06 |
| OnAccent / Accent (Ungu) | 7.64 | 5.87 |
| Positive / Bg · Negative / Bg | 9.43 · 8.31 | 4.99 · 5.03 |
| Weakest preset: accent / Window | Ungu 7.23 | Hijau 4.98 |
| Weakest preset: OnAccent / accent | Ungu 7.64 | Hijau 5.29 |
| Glass: Sub over a white-backed veil, `OnGlass` | ≥ 4.60 | — |

## Shape

Corners are squircles (`Ink.AddRound`, smoothing 0.6). Radii nest: an inner radius equals the outer radius minus the inset.

| Token | px @1x | Use |
|---|---|---|
| `RadiusShell` | 32 | island shell |
| `RadiusModule` | 20 | island modules, dock sheets |
| `RadiusCard` | 16 | cards outside Settings (task board, notes) |
| `RadiusList` | 14 | Settings groups (`KitGroup`) and the page hero card; under the hero tile's nest limit of 11 + 14 inset |
| `RadiusControl` | 12 | buttons, check rows, segmented track, slider focus; capped at 0.35 × height, so 30 px Settings controls get 10 |
| category tile | 5 (20 px) · 11 (44 px) | sidebar tiles and the hero tile |
| inner thumb | 12 − 3 = 9 | segmented thumb; settings icon tiles |
| tick box | 5 | `KitCheck` box |
| pill | h / 2 | switches, chips, progress tracks only |

Pills are for small toggles and tracks only. Buttons are soft squircles, not capsules.

## Spacing and size

- Scale: `Ink.Sp4 … Sp32` (4, 8, 12, 16, 20, 24, 32). Groups are tight (8–12) and sections are well separated (24–32).
- Module grid: inset 12, gap 10, padding 16.
- Hit targets: buttons, segmented controls, popups, swatches, hotkey fields and dialog actions are **36 px** high outside Settings. Settings is denser: controls are **30 px** high and switches 38 × 22, still above the 24 px WCAG 2.2 minimum target.
- Settings rows: 44 px for one line, 54 px for title plus detail, with both lines centred as one block. Taller custom rows (theme, material, place, QR) top-align their text beside the glyph.
- Settings page rhythm: the content column is at most 720 px wide and centred. 20 to the hero card (72 high), then 24 to the content. A section heading sits 8 above its group; groups are 20 apart. A merged page starts each later part with a part title (Title2) 12 below the last group. A guidance note under a heading uses the body weight.
- All Kit geometry scales by `KitWindow.Ui` (per-monitor DPI).

## Type

Segoe UI Variable (with Segoe UI fallback), from `Ink`:

| Role | Face | px |
|---|---|---|
| Hero | Display Semibold | 40 |
| Hero / search results title (Settings) | Text Semibold | 15 / Display Semibold 20 |
| Title1 | Display Semibold | 22 |
| Title2 / part title | Text Semibold | 17 |
| Headline | Text Semibold | 14.5 |
| Body | Text | 13.5 |
| Settings row title / side rail label | Text | 13 |
| Callout / control label | Text | 12.5 |
| Row detail | Text | 11.5 |
| Section heading (Settings) | Text Semibold | 13, `Text` |
| Caption | Small | 11 (smallest reading size; 9.5 only for the version suffix) |

The selected segment and the selected side-rail item use Text Semibold. Weight carries emphasis, never color or gradient text.

## Settings (macOS System Settings pattern, Deskmate palette)

Color is for navigation only: every category owns a tile color, and rows inside a page stay monochrome.

### Category tiles

Tiles reuse the paper twins of `Ink.Labels` in both themes (`Ink.Label(key, true)`), so no new literal exists. The glyph is `Ink.On(tile)`, which is ivory on every twin (≥ 4.9:1). The same color fills the 20 px sidebar tile and the 44 px hero tile of that page.

### Side rail

- 220 px wide on `Side`. The search well (28 px, radius 7) sits on top.
- The first item is the Deskmate card: app icon, name and version, 58 px high. It opens Tentang.
- Rows are 30 px with a 7 px radius: tile, 10 px gap, label (Text 13). Groups are separated by 14 px of space, without headings.
- Hover is `Side` mixed 5–6 % toward `Text`. The selected row is filled with the accent, and its label is `OnAccent` in semibold; the tile keeps its color. Keyboard focus uses the shared focus ring.

### Page

- A hero card (`Group`, `RadiusList`, 72 px) opens every page: 44 px category tile, title (Text Semibold 15), and a one-line note in `Sub`.
- Rows have no tiles: an optional 16 px glyph in `Sub` leads, the text starts at 44 px, and hairlines start where the text starts.
- Pages that merge older ones (Island + tab, Tugas + integrasi, AI + dikte, Perangkat + HP) keep each part's own headings and add a part title before every later part. Old ids still open the right page and scroll to their part.

## Elevation

Each element declares elevation once, either by tone step or by shadow. Never use both a border and a shadow.

- **0, canvas:** `Window`.
- **1, card:** `Group` is one tone step off the canvas, with no border and no shadow. A selected sidebar row is the only filled navigation surface.
- **2, raised thumb or knob:** `KitInk.Lift`, a contact shadow at 0.75 px below plus a falloff at 1.5 px below. Paper uses plum `#2A2038` at α 26/13; night uses black at α 90/45. It always has an offset and is never a halo. On paper, the white slider knob also gets a 1 px `Line` edge so it reads on ivory.
- **3, floating:** island glass and menus. These use the glass materials (`Glass.cs`) and the window shadow, not Kit.

### Calm material (`material=calm`)

The quietest glass: the pane is the wallpaper in shade, not a coloured sheet over it.

- Pane: the veil's hue at 0.85 saturation, mixed 22 % toward black (night) or 18 % toward white (paper) (`GlassRecipe.CalmTint`). No accent in the pane, no sheen, almost no top wash.
- Edge: one even hairline. The top rim is only slightly brighter than the sides (night 58 / 42, paper 104 / 80).
- Software glass (`CalmVeil`, shared by island and dock) blurs twice, mutes the backdrop, and lays an even veil that thickens only where text would drop under its contrast floor.
- Kit windows (Settings, editors) take `KitInk.Canvas`: the wallpaper hue at low chroma, held at the theme tone's luminance (paper + 0.02 headroom), so every ink mix keeps the floors above. Calm reads the wallpaper hue even with `wall.sync` off; theme packs override it. Verified ≥ 4.5:1 for Text, Sub, and Faint on every Kit surface across 12 hues in both themes (`test_b06`).

## States

| State | Treatment |
|---|---|
| Hover, secondary | `Tint(0.17)` |
| Pressed, secondary | `Tint(0.22)`, body shifts 0.75 px down |
| Hover / pressed, primary | `Deepen(accent, OnAccent, 0.10 / 0.20)` moves the fill away from its label ink, so contrast only grows |
| Selected segment | `Raised` thumb with Lift and a 3 px inset. The thumb glides between segments |
| Unselected segment hover | label goes `Sub` → `Text` |
| Switch | off: `Line` track with a `Sub` knob (hover lifts the knob toward `Text`). On: accent track with an `OnAccent` knob. Disabled: `Line` track keeps 30 % of the accent when on, with a `Disabled` knob |
| Check | off: 1.5 px `Sub` outline. On: accent box with an `OnAccent` tick |
| Disabled | `Line` fill, `Faint` label (still ≥ 4.5:1), `Disabled` knobs and marks, palette swatches 55 % toward the canvas |

## Focus ring

Keyboard only (`KitInk.ByMouse` hides it after pointer use). `KitInk.FocusRing` draws a 2 px ring in `Focus`, with a 1.25 px hairline of the surrounding surface just inside it. The ring meets ≥ 3:1 against the canvas, and the hairline separates it from any fill, including an accent-filled primary button. Buttons and check rows shrink their body by 3.5 px while focused so the ring sits outside. Other controls draw it 2.5 px outside their shape, clamped to bounds. The palette rings the swatch that the arrow keys move.

## Motion

- Switches (150 ms) and segmented choices (180 ms) use elapsed-time cubic ease-out scaled by the motion pace. A new choice redirects the thumb from its displayed position. The switch knob stretches slightly in transit and is round at rest. Window arrival is a 160 ms ease-out-quad fade.
- Every animation snaps to its end state when `Anim.Reduced` is set (the Windows "animation effects" setting).
- Nothing loops or pulses inside Kit.

## Drawing rules (GDI+)

- `KitInk.Smooth(g)`: AntiAlias, ClearTypeGridFit, PixelOffsetMode.HighQuality.
- Kit paints allocate nothing. Use `KitInk.BrushOf` / `PenOf` / `Shape` (per-thread, reused), `FillRound` / `StrokeRound`, and cached fonts (`Ink.Text/Semi/Icons(px)`). Never `new Font` or `new SolidBrush` in `OnPaint`.
- Read colors from `Ink.Base` / `KitInk`. A literal color is allowed only as an identity value inside `Ink.cs` or `KitInk`.
