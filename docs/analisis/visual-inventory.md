# Inventaris literal visual (bypass design token)

Dibuat 2026-11-18 dari pemindaian statis `src/*.cs` (254 file .cs). Analisis read-only; tidak ada file sumber yang diubah.

> **Status:** ini potret **sebelum** migrasi palet tenang; angka di bawah adalah baseline, bukan kondisi sekarang. Sejak itu `Ink.Fx*`, `Ink.Markers`, `Ink.Labels`, `Ink.Scrim`/`Ink.Sheen`, `Ink.Warning`, dan `Ink.On` dipakai; `ComboFx.cs`, `ComboInput.cs`, dan `PrefsThemePicker.cs` turun ke 0 warna RGB literal, dan `Ink.Sp*` kini dipakai 52× (sebelumnya 0). Sisa warna literal adalah overlay gambar/glass, bayangan, dan jangkar matematika kontras yang memang tidak boleh ikut tema. Kontrak yang berlaku ada di `DESIGN.md`.

## Temuan kunci

- Warna literal terkonsentrasi di sedikit file: ComboFx (34), PrefsThemePicker (31), RedactWindowModel (23), RedactWindow (20), Prefs (14 hex), PrefsWindow (10 hex) = 132 dari 187 RGB+hex (~71%).
- Pola terbanyak adalah putih/hitam dengan alpha ajaib (`FromArgb(a, 255,255,255)` 28×, `FromArgb(a, 0,0,0)` 21×) — kandidat token overlay/scrim (mis. `Tone.Scrim`, `Tone.Sheen`) atau dirutekan lewat `Painter.cs:202` yang sudah memilih hitam/putih per tema.
- Palet "sistem" Apple-ish berulang di Combo/EdgeLight (#32ADE6, #FFD60A, #30D158, #FF375F, #FF9F0A, #FF505A) — kandidat token palet efek (`Ink.Fx*`).
- Palet label Tailwind-ish dipakai ganda: `NoteControls.cs:16–20` dan `RedactWindowModel.cs:377–389` (#10B981, #0EA5E9, #8B5CF6, #3B82F6, …).
- Accent preset diduplikasi: `Prefs.cs:220–238` mengulang hex `Ink.DarkAccents/LightAccents` (Ink.cs:110–132) plus 2 nilai yang tidak ada di Ink (`#FF5252`, `#C5221F`); `RedactWindow.cs:49–50` mengulang lagi sebagian.
- `Ink.Sp*` tidak dipakai sama sekali (0×) vs ±1091 angka spasi literal; `Ink.Radius*` 26× di helper rounded vs 231 radius literal (19× `12` = RadiusControl yang ditulis literal).
- `new Font(` 54× — mayoritas family dari token (`Ink.MenuFamily/IconFamily`) tapi ukuran literal; 614 pemakaian helper font Ink + 67 `UiFont(`.

## Metode

- **Warna hard-coded (RGB)**: `Color.FromArgb(...)` dengan 3 komponen RGB terakhir berupa literal numerik (alpha boleh variabel), atau `FromArgb(int)` literal; ditambah string hex `"#RRGGBB"` / `ColorTranslator.*`. Kolom tabel = RGB + hex.
- **alpha-on-token**: `Color.FromArgb(<literal>, <warna lain>)` — alpha ajaib di atas token/warna lain (dihitung terpisah, bukan warna baru).
- **Warna bernama**: `Color.<KnownColor>` (White, Black, Transparent, …) dan `SystemColors.*`.
- **Token source** dikecualikan dari hitungan: `Ink.cs` baris 12–88 (`Tone`) dan 100–233 (accent preset / `Apply`), `Kit.cs` 12–36 (`KitInk`). Didaftar terpisah di bawah.
- **Helper rounded-rect** yang dipindai (argumen radius): `Ink.Round(r, R)`, `Ink.AddRound(p, r, R)`, `Ink.Notch/SideNotch/AddNotch`, `Painter.FillRound(r, R, …)` / `StrokeRound(r, R, …)`, `TaskInk.Pill(g, r, R, c)` (TaskControls.cs:76), `Round(g, r, R, c)` lokal (AgentPromptHeader.cs:128). Klasifikasi radius: *token* (`Ink.Radius*`), *pill/komputasi* (berisi Height/Width/`/2`/Min), *literal* (angka, termasuk `N * s`), *variabel*.
- **Font**: `new Font(` (semua), string family (`"Segoe …"`, dll. di luar `Ink.Init`), dan pemakaian helper `Ink.Display/Text/Semi/Small/SmallSemi/Mono/Icons/Hero/Title1/Title2/Headline/Body/Callout/Caption` + `KitWindow.UiFont(`.
- **Token usages**: `KitInk.<warna>`, `Ink.Base/Flip.<tone>`, `T.<tone>` (Painter), `Ink.Radius*`, `Ink.Sp*`, `Ink.Module*`, `Ink.Veil`, helper font Ink.
- **Spasi literal (kasar)**: angka 4/8/12/16/20/24/32 (opsional `f`) pada baris layout (Padding/Inflate/Rectangle/Size/Point/Width/Height/X/Y/…), di luar baris warna/font/radius. Heuristik — angka bisa saja bukan spasi.

## Total

| Metrik | Jumlah |
|---|---|
| `FromArgb` RGB literal | 149 |
| Hex string / ColorTranslator | 38 |
| alpha-on-token `FromArgb(n, c)` | 33 |
| Warna bernama / SystemColors | 83 |
| `new Font(` | 54 |
| String family font di luar Ink.Init | 11 |
| Helper font Ink (Ink.Body, Ink.Semi(px), …) | 614 |
| `UiFont(` (KitWindow) | 67 |
| Radius literal | 231 |
| Radius `Ink.Radius*` | 26 |
| Radius pill/komputasi | 79 |
| Radius variabel | 44 |
| Spasi literal 4–32 (kasar) | 1091 |
| `Ink.Sp*` | 0 |
| Total token usages | 1445 |

Catatan: `Ink.Sp4…Sp32` didefinisikan (Ink.cs:248) tetapi **0 pemakaian**; `Ink.ModuleInset/Gap/Pad` dipakai 31×. `Ink.RadiusShell` dipakai 1×, `RadiusModule` 16×, `RadiusControl` 29× (semua call-site, termasuk di luar helper rounded). Di antara radius literal, `12` (=RadiusControl) muncul 19× dan `12 * s` 6×; `20` (=RadiusModule) 3×.

## Ringkasan per file

Diurutkan menurut warna hard-coded (RGB+hex) menurun; hanya file dengan ≥1 temuan. Kolom tambahan: alpha-on-token, radius pill, spasi literal kasar.

| File | Hardcoded colors | Named colors | new Font | Literal radii | Token usages | alpha-on-token | Radius token | Spasi literal |
|---|---|---|---|---|---|---|---|---|
| ComboFx.cs | 34 | 3 | 0 | 20 | 23 | 0 | 0 | 16 |
| PrefsThemePicker.cs | 31 | 7 | 0 | 3 | 24 | 2 | 0 | 12 |
| RedactWindowModel.cs | 23 | 2 | 2 | 1 | 0 | 6 | 0 | 1 |
| RedactWindow.cs | 20 | 5 | 0 | 1 | 12 | 2 | 0 | 29 |
| Prefs.cs | 14 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| PrefsWindow.cs | 10 | 0 | 5 | 2 | 24 | 0 | 0 | 13 |
| Glass.cs | 8 | 3 | 0 | 0 | 10 | 0 | 0 | 2 |
| ComboInput.cs | 7 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Music.cs | 6 | 0 | 0 | 2 | 7 | 1 | 0 | 12 |
| Colors.cs | 5 | 1 | 0 | 0 | 4 | 2 | 0 | 0 |
| NoteControls.cs | 5 | 0 | 0 | 4 | 41 | 0 | 1 | 31 |
| KitPickers.cs | 4 | 1 | 1 | 1 | 13 | 1 | 0 | 3 |
| EdgeLight.cs | 4 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| KitButtons.cs | 3 | 2 | 4 | 3 | 39 | 0 | 0 | 9 |
| Painter.cs | 2 | 8 | 1 | 3 | 6 | 1 | 0 | 5 |
| Clips.cs | 2 | 2 | 0 | 5 | 3 | 4 | 0 | 15 |
| DockPaint.cs | 2 | 1 | 0 | 1 | 3 | 5 | 0 | 2 |
| GlassKind.cs | 2 | 1 | 0 | 0 | 2 | 0 | 0 | 0 |
| GoogleCal.cs | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| IslandRender.cs | 1 | 2 | 0 | 1 | 5 | 0 | 0 | 5 |
| ColorPicker.cs | 1 | 2 | 0 | 0 | 7 | 1 | 0 | 5 |
| SuruhChat.cs | 1 | 1 | 0 | 0 | 4 | 0 | 0 | 1 |
| Overview.cs | 0 | 7 | 0 | 12 | 46 | 0 | 0 | 61 |
| DockSheet.cs | 0 | 5 | 0 | 5 | 15 | 1 | 0 | 9 |
| RedactToolStrip.cs | 0 | 4 | 1 | 1 | 8 | 2 | 0 | 0 |
| DockCard.cs | 0 | 2 | 0 | 5 | 5 | 1 | 0 | 20 |
| Parts.cs | 0 | 2 | 0 | 3 | 25 | 0 | 1 | 21 |
| Kit.cs | 0 | 2 | 2 | 1 | 18 | 0 | 0 | 1 |
| AlbumAccent.cs | 0 | 2 | 0 | 0 | 1 | 0 | 0 | 0 |
| DockPaintSpan.cs | 0 | 2 | 0 | 0 | 1 | 0 | 0 | 0 |
| PrefsPages.cs | 0 | 2 | 0 | 0 | 2 | 0 | 0 | 6 |
| Qr.cs | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 2 |
| WallBackdrop.cs | 0 | 2 | 0 | 0 | 1 | 0 | 0 | 4 |
| WallSync.cs | 0 | 2 | 0 | 0 | 2 | 0 | 0 | 0 |
| Views.cs | 0 | 1 | 0 | 4 | 87 | 0 | 7 | 47 |
| Reminders.cs | 0 | 1 | 0 | 2 | 34 | 0 | 1 | 20 |
| CompGlass.cs | 0 | 1 | 0 | 0 | 1 | 0 | 0 | 1 |
| ConverterImage.cs | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 1 |
| Mini.cs | 0 | 1 | 0 | 0 | 1 | 0 | 0 | 4 |
| Place.cs | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 1 |
| PrefsLayout.cs | 0 | 1 | 0 | 0 | 5 | 0 | 0 | 1 |
| ScheduleDialogFields.cs | 0 | 1 | 0 | 0 | 36 | 0 | 0 | 3 |
| DockQuick.cs | 0 | 0 | 0 | 12 | 22 | 0 | 0 | 39 |
| DockStart.cs | 0 | 0 | 0 | 11 | 10 | 0 | 0 | 18 |
| Prompter.cs | 0 | 0 | 0 | 11 | 7 | 0 | 0 | 6 |
| DockCal.cs | 0 | 0 | 0 | 8 | 19 | 0 | 0 | 23 |
| Schedule.cs | 0 | 0 | 0 | 8 | 30 | 0 | 1 | 21 |
| TaskView.cs | 0 | 0 | 1 | 7 | 50 | 0 | 1 | 34 |
| ControlWifi.cs | 0 | 0 | 0 | 6 | 12 | 0 | 0 | 13 |
| DockAppMenu.cs | 0 | 0 | 0 | 6 | 7 | 0 | 0 | 15 |
| Tabs.cs | 0 | 0 | 0 | 6 | 2 | 0 | 0 | 11 |
| Handset.cs | 0 | 0 | 0 | 5 | 27 | 0 | 0 | 17 |
| ScheduleDialogPickers.cs | 0 | 0 | 5 | 4 | 42 | 0 | 0 | 11 |
| DockPaintLeft.cs | 0 | 0 | 0 | 4 | 14 | 0 | 0 | 9 |
| DockWeather.cs | 0 | 0 | 0 | 4 | 18 | 0 | 0 | 2 |
| Side.cs | 0 | 0 | 0 | 4 | 8 | 0 | 0 | 7 |
| Suara.cs | 0 | 0 | 0 | 4 | 11 | 0 | 0 | 21 |
| PrompterControls.cs | 0 | 0 | 3 | 3 | 13 | 0 | 0 | 8 |
| ConvertView.cs | 0 | 0 | 0 | 3 | 5 | 0 | 0 | 4 |
| WhatsNew.cs | 0 | 0 | 6 | 2 | 12 | 0 | 0 | 4 |
| Integrations.cs | 0 | 0 | 1 | 2 | 6 | 0 | 0 | 2 |
| PrefsCitySuggest.cs | 0 | 0 | 1 | 2 | 5 | 0 | 0 | 15 |
| PrefsTabs.cs | 0 | 0 | 1 | 2 | 4 | 0 | 0 | 3 |
| AgentPromptHeader.cs | 0 | 0 | 0 | 2 | 42 | 1 | 0 | 3 |
| ControlWidgets.cs | 0 | 0 | 0 | 2 | 24 | 0 | 0 | 16 |
| DockPaintChrome.cs | 0 | 0 | 0 | 2 | 4 | 0 | 0 | 4 |
| Downloads.cs | 0 | 0 | 0 | 2 | 5 | 0 | 0 | 0 |
| Locks.cs | 0 | 0 | 0 | 2 | 6 | 0 | 0 | 0 |
| Panel.cs | 0 | 0 | 0 | 2 | 34 | 0 | 6 | 32 |
| Shelf.cs | 0 | 0 | 0 | 2 | 11 | 0 | 4 | 22 |
| TaskControls.cs | 0 | 0 | 0 | 2 | 44 | 0 | 0 | 21 |
| TaskDragPreview.cs | 0 | 0 | 0 | 2 | 7 | 0 | 0 | 0 |
| Menus.cs | 0 | 0 | 3 | 1 | 12 | 1 | 0 | 15 |
| PrefsDikte.cs | 0 | 0 | 1 | 1 | 9 | 0 | 0 | 10 |
| AgentPromptUi.cs | 0 | 0 | 0 | 1 | 23 | 0 | 0 | 17 |
| Control.cs | 0 | 0 | 0 | 1 | 18 | 0 | 0 | 1 |
| ControlWifiPass.cs | 0 | 0 | 0 | 1 | 11 | 0 | 0 | 9 |
| ConvertPage.cs | 0 | 0 | 0 | 1 | 9 | 0 | 2 | 22 |
| Dictate.cs | 0 | 0 | 0 | 1 | 10 | 1 | 0 | 8 |
| DockInputMenu.cs | 0 | 0 | 0 | 1 | 1 | 0 | 0 | 2 |
| DockPaintFlyer.cs | 0 | 0 | 0 | 1 | 1 | 0 | 0 | 2 |
| Feedback.cs | 0 | 0 | 0 | 1 | 10 | 0 | 0 | 8 |
| Keyboard.cs | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 1 |
| Mirror.cs | 0 | 0 | 0 | 1 | 1 | 0 | 0 | 4 |
| Notes.cs | 0 | 0 | 0 | 1 | 7 | 0 | 0 | 8 |
| Rapat.cs | 0 | 0 | 0 | 1 | 4 | 0 | 0 | 6 |
| Shots.cs | 0 | 0 | 0 | 1 | 2 | 0 | 0 | 3 |
| SuruhChatList.cs | 0 | 0 | 0 | 1 | 39 | 0 | 0 | 25 |
| TaskBoardLane.cs | 0 | 0 | 0 | 1 | 33 | 0 | 0 | 11 |
| Tour.cs | 0 | 0 | 0 | 1 | 9 | 0 | 0 | 12 |
| WorkWindow.cs | 0 | 0 | 0 | 1 | 51 | 0 | 1 | 18 |
| NoteEditor.cs | 0 | 0 | 8 | 0 | 8 | 0 | 0 | 1 |
| PrefsSideList.cs | 0 | 0 | 3 | 0 | 13 | 0 | 0 | 5 |
| KitWindow.cs | 0 | 0 | 2 | 0 | 8 | 0 | 0 | 3 |
| RedactWindowSelect.cs | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 4 |
| RedactWindowText.cs | 0 | 0 | 1 | 0 | 3 | 0 | 0 | 5 |
| ScheduleDialog.cs | 0 | 0 | 1 | 0 | 15 | 0 | 0 | 0 |
| Converter.cs | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| DockApps.cs | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 2 |
| DockKeys.cs | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| Ink.cs | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| NotifOnce.cs | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Notifs.cs | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| TaskNoteEditor.cs | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 10 |
| TaskNoteMarkdown.cs | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 3 |

## Pelanggar teratas per grup permukaan

Format: `literal` ×jumlah di grup — lokasi `file:baris`. Pola paling sering di atas; `Color.Transparent` dan `FromArgb(255, c)` (opaque-ify) tetap dicatat tapi relatif aman.

### Prefs*

File: 13 · RGB+hex 55 · named 10 · alpha-on-token 2 · new Font 11 · radius literal 10 / token 0 · token usages 93

- [named] `Color.White` ×4 — PrefsThemePicker.cs:234, PrefsThemePicker.cs:517, PrefsThemePicker.cs:518, PrefsThemePicker.cs:545
- [named] `Color.Transparent` ×3 — PrefsLayout.cs:265, PrefsPages.cs:531, PrefsPages.cs:536
- [named] `Color.Black` ×3 — PrefsThemePicker.cs:234, PrefsThemePicker.cs:517, PrefsThemePicker.cs:518
- [RGB] `Color.FromArgb(38, 255, 255, 255)` ×2 — PrefsThemePicker.cs:282, PrefsThemePicker.cs:318
- [RGB] `Color.FromArgb(0, 255, 255, 255)` ×2 — PrefsThemePicker.cs:520, PrefsThemePicker.cs:539
- [hex] `"#FFFFFF"` ×2 — PrefsWindow.cs:792, PrefsWindow.cs:793
- [radius] `Ink.Round(…, 9 * s` ×2 — PrefsTabs.cs:54, PrefsTabs.cs:60
- [radius] `Ink.Round(…, 10 * s` ×2 — PrefsThemePicker.cs:216, PrefsThemePicker.cs:235
- [hex] `"#4C8DFF"` ×1 — Prefs.cs:220
- [hex] `"#A78BFA"` ×1 — Prefs.cs:221
- [hex] `"#FF6B9A"` ×1 — Prefs.cs:222
- [hex] `"#FF5252"` ×1 — Prefs.cs:223
- [hex] `"#FF9F43"` ×1 — Prefs.cs:224
- [hex] `"#FFD24A"` ×1 — Prefs.cs:225
- [hex] `"#3DD68C"` ×1 — Prefs.cs:226
- [hex] `"#1668E8"` ×1 — Prefs.cs:232
- [hex] `"#6C3CE0"` ×1 — Prefs.cs:233
- [hex] `"#C2185B"` ×1 — Prefs.cs:234
- [hex] `"#C5221F"` ×1 — Prefs.cs:235
- [hex] `"#A84B00"` ×1 — Prefs.cs:236
- [hex] `"#855E00"` ×1 — Prefs.cs:237
- [hex] `"#107C41"` ×1 — Prefs.cs:238
- [RGB] `Color.FromArgb(255, 28, 31, 37)` ×1 — PrefsThemePicker.cs:21
- [RGB] `Color.FromArgb(255, 14, 16, 20)` ×1 — PrefsThemePicker.cs:22
- [RGB] `Color.FromArgb(34, 255, 255, 255)` ×1 — PrefsThemePicker.cs:225
- … 51 pola lain (masing-masing lebih jarang)

### Work* / Task* / Note*

File: 17 · RGB+hex 5 · named 0 · alpha-on-token 0 · new Font 9 · radius literal 18 / token 3 · token usages 275

- [radius] `TaskInk.Pill(…, 1.5f * s` ×3 — NoteControls.cs:518, NoteControls.cs:520, TaskControls.cs:427
- [radius] `TaskInk.Pill(…, 10 * s` ×3 — TaskControls.cs:426, TaskView.cs:388, TaskView.cs:401
- [radius] `TaskInk.Pill(…, 9 * s` ×3 — TaskView.cs:435, TaskView.cs:442, TaskView.cs:443
- [radius] `TaskInk.Pill(…, 8 * s` ×2 — NoteControls.cs:533, NoteControls.cs:534
- [radius] `TaskInk.Pill(…, box * 0.3f` ×2 — TaskView.cs:371, WorkWindow.cs:676
- [RGB] `Color.FromArgb(245, 158, 11)` ×1 — NoteControls.cs:16
- [RGB] `Color.FromArgb(16, 185, 129)` ×1 — NoteControls.cs:17
- [RGB] `Color.FromArgb(14, 165, 233)` ×1 — NoteControls.cs:18
- [RGB] `Color.FromArgb(139, 92, 246)` ×1 — NoteControls.cs:19
- [RGB] `Color.FromArgb(244, 63, 94)` ×1 — NoteControls.cs:20
- [radius] `Ink.Round(…, 12 * s` ×1 — Notes.cs:146
- [radius] `Ink.Round(…, 7 * s` ×1 — TaskBoardLane.cs:185
- [radius] `Ink.Round(…, 9 * scale` ×1 — TaskDragPreview.cs:28
- [radius] `Ink.Round(…, 8 * scale` ×1 — TaskDragPreview.cs:76
- [radius] `TaskInk.Pill(…, 11 * s` ×1 — TaskView.cs:462
- [new Font] `new Font(Font.FontFamily, Font.Size * 1.52f, FontStyle.Bold)` ×1 — NoteEditor.cs:227
- [new Font] `new Font(Font.FontFamily, Font.Size * 1.3f, FontStyle.Bold)` ×1 — NoteEditor.cs:228
- [new Font] `new Font(Font.FontFamily, Font.Size * 1.12f, FontStyle.Bold)` ×1 — NoteEditor.cs:229
- [new Font] `new Font(Font, FontStyle.Bold)` ×1 — NoteEditor.cs:230
- [new Font] `new Font(Font, FontStyle.Italic)` ×1 — NoteEditor.cs:231
- [new Font] `new Font(Font, FontStyle.Bold | FontStyle.Italic)` ×1 — NoteEditor.cs:232
- [new Font] `new Font(Font, FontStyle.Strikeout)` ×1 — NoteEditor.cs:233
- [new Font] `new Font(FontFamily.GenericMonospace, Font.Size)` ×1 — NoteEditor.cs:234
- [family string] `"Segoe UI"` ×1 — TaskNoteEditor.cs:24
- [family string] `"Consolas"` ×1 — TaskNoteMarkdown.cs:15
- … 1 pola lain (masing-masing lebih jarang)

### Island* / Panel / Views / Overview

File: 8 · RGB+hex 1 · named 10 · alpha-on-token 0 · new Font 0 · radius literal 19 / token 13 · token usages 172

- [radius] `FillRound(…, 1.5f` ×8 — Overview.cs:384, Overview.cs:385, Overview.cs:570, Overview.cs:572, Overview.cs:664, Overview.cs:667, Overview.cs:811, Views.cs:646
- [named] `Color.White` ×5 — Overview.cs:649, Overview.cs:651, Overview.cs:724, Overview.cs:783, Overview.cs:785
- [named] `Color.Transparent` ×3 — IslandRender.cs:49, IslandRender.cs:369, Views.cs:389
- [radius] `FillRound(…, 5f` ×2 — Overview.cs:736, Overview.cs:746
- [radius] `FillRound(…, 11` ×2 — Panel.cs:228, Panel.cs:364
- [radius] `FillRound(…, 1` ×2 — Views.cs:388, Views.cs:389
- [named] `Color.Black` ×2 — Overview.cs:649, Overview.cs:783
- [RGB] `Color.FromArgb((int)(ShadowAlpha[i] * strength), 0, 0, 0)` ×1 — IslandRender.cs:411
- [radius] `Ink.Round(…, Math.Max(0, snapRadius * sc - SnapInset)` ×1 — IslandRender.cs:220
- [radius] `FillRound(…, 20` ×1 — Overview.cs:355
- [radius] `FillRound(…, 6f` ×1 — Overview.cs:713
- [radius] `StrokeRound(…, 7f` ×1 — Overview.cs:728
- [radius] `FillRound(…, 12` ×1 — Views.cs:632

### Dock*

File: 25 · RGB+hex 2 · named 10 · alpha-on-token 7 · new Font 0 · radius literal 60 / token 0 · token usages 123

- [named] `Color.Transparent` ×8 — DockCard.cs:405, DockPaint.cs:45, DockPaintSpan.cs:60, DockSheet.cs:476, DockSheet.cs:490, DockSheet.cs:500, DockSheet.cs:515, DockSheet.cs:734
- [radius] `FillRound(…, 10` ×7 — DockQuick.cs:192, DockQuick.cs:356, DockSheet.cs:774, DockStart.cs:610, DockStart.cs:660, DockStart.cs:670, DockStart.cs:674
- [alpha-on-token] `Color.FromArgb(0, p.T.Highlight)` ×7 — DockCard.cs:433, DockPaint.cs:234, DockPaint.cs:245, DockPaint.cs:245, DockPaint.cs:249, DockPaint.cs:249, DockSheet.cs:521
- [radius] `FillRound(…, 12` ×6 — DockCal.cs:238, DockPaintFlyer.cs:57, DockPaintLeft.cs:110, DockStart.cs:543, DockStart.cs:598, DockStart.cs:637
- [radius] `FillRound(…, 1.5f` ×5 — DockAppMenu.cs:444, DockCal.cs:146, DockPaintLeft.cs:225, DockPaintLeft.cs:226, DockStart.cs:619
- [radius] `FillRound(…, 8` ×5 — DockCal.cs:101, DockCard.cs:593, DockCard.cs:630, DockStart.cs:564, DockStart.cs:603
- [radius] `FillRound(…, 14` ×5 — DockCal.cs:117, DockQuick.cs:243, DockQuick.cs:260, DockQuick.cs:304, DockQuick.cs:327
- [radius] `FillRound(…, 9` ×5 — DockCal.cs:123, DockInputMenu.cs:223, DockPaintChrome.cs:61, DockPaintLeft.cs:181, DockSheet.cs:762
- [radius] `FillRound(…, 2` ×5 — DockQuick.cs:134, DockQuick.cs:135, DockWeather.cs:88, DockWeather.cs:128, DockWeather.cs:130
- [radius] `FillRound(…, 6` ×3 — DockCal.cs:165, DockCal.cs:256, DockCard.cs:589
- [radius] `FillRound(…, 15` ×3 — DockCal.cs:282, DockQuick.cs:271, DockQuick.cs:336
- [RGB] `Color.FromArgb(1, 0, 0, 0)` ×2 — DockPaint.cs:115, DockPaint.cs:124
- [radius] `FillRound(…, 7f` ×2 — DockAppMenu.cs:429, DockAppMenu.cs:512
- [radius] `StrokeRound(…, 9` ×2 — DockPaintChrome.cs:62, DockSheet.cs:763
- [family string] `"Microsoft.Windows.Explorer"` ×2 — DockAppMenu.cs:170, DockApps.cs:223
- [named] `Color.White` ×2 — DockCard.cs:590, DockPaintSpan.cs:204
- [radius] `FillRound(…, 8f` ×1 — DockAppMenu.cs:356
- [radius] `FillRound(…, 9f` ×1 — DockAppMenu.cs:418
- [radius] `FillRound(…, 11f` ×1 — DockAppMenu.cs:470
- [radius] `FillRound(…, 11` ×1 — DockCard.cs:579
- [radius] `FillRound(…, 7` ×1 — DockCard.cs:645
- [radius] `FillRound(…, 1.4f * u` ×1 — DockPaint.cs:320
- [radius] `FillRound(…, 16` ×1 — DockQuick.cs:349
- [radius] `StrokeRound(…, 10` ×1 — DockQuick.cs:357
- [radius] `Ink.Round(…, 20` ×1 — DockSheet.cs:472
- … 5 pola lain (masing-masing lebih jarang)

### Schedule* / Prompter* / SuruhChat* / Redact* / Tour / WhatsNew / Kit* / AgentPrompt*

File: 30 · RGB+hex 51 · named 18 · alpha-on-token 12 · new Font 29 · radius literal 41 / token 1 · token usages 403

- [named] `Color.White` ×10 — KitPickers.cs:159, RedactToolStrip.cs:284, RedactToolStrip.cs:284, RedactWindow.cs:782, RedactWindow.cs:801, RedactWindow.cs:811, RedactWindow.cs:824, RedactWindow.cs:839, … (+2)
- [new Font] `new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel)` ×6 — KitButtons.cs:235, KitPickers.cs:233, ScheduleDialogPickers.cs:35, WhatsNew.cs:267, WhatsNew.cs:301, WhatsNew.cs:304
- [named] `Color.Transparent` ×5 — Kit.cs:133, Kit.cs:184, KitButtons.cs:22, KitButtons.cs:218, ScheduleDialogFields.cs:100
- [radius] `Ink.Round(…, 6 * s` ×4 — AgentPromptUi.cs:87, PrompterControls.cs:129, ScheduleDialogPickers.cs:272, ScheduleDialogPickers.cs:286
- [radius] `FillRound(…, 1` ×4 — Prompter.cs:437, Prompter.cs:438, Prompter.cs:490, Prompter.cs:491
- [radius] `Ink.Round(…, 12 * s` ×3 — KitButtons.cs:186, PrompterControls.cs:116, WhatsNew.cs:299
- [radius] `FillRound(…, 0.8f` ×3 — Prompter.cs:480, Prompter.cs:494, Prompter.cs:495
- [radius] `FillRound(…, 1.5f` ×3 — Prompter.cs:638, Prompter.cs:639, Prompter.cs:640
- [radius] `FillRound(…, 13` ×3 — Schedule.cs:167, Schedule.cs:168, Schedule.cs:331
- [new Font] `new Font(Ink.MenuFamily, 13.5f * s, GraphicsUnit.Pixel)` ×3 — KitButtons.cs:234, WhatsNew.cs:266, WhatsNew.cs:303
- [named] `Color.Black` ×3 — RedactToolStrip.cs:284, RedactToolStrip.cs:284, RedactWindowModel.cs:305
- [RGB] `Color.FromArgb(255, 59, 130, 246)` ×2 — KitButtons.cs:86, RedactWindowModel.cs:377
- [hex] `ColorTranslator.FromHtml(` ×2 — KitPickers.cs:136, RedactWindow.cs:154
- [hex] `"#FF3B30"` ×2 — RedactWindow.cs:49, RedactWindow.cs:154
- [radius] `Ink.Round(…, 8 * s` ×2 — KitButtons.cs:255, WhatsNew.cs:318
- [radius] `Ink.Round(…, 7 * s` ×2 — ScheduleDialogPickers.cs:106, ScheduleDialogPickers.cs:109
- [new Font] `new Font(Ink.MenuFamily, 12.5f * Ui, FontStyle.Regular, GraphicsUnit.Pixel)` ×2 — Kit.cs:286, Kit.cs:287
- [new Font] `new Font(Ink.IconFamily, 15f * s, GraphicsUnit.Pixel)` ×2 — KitButtons.cs:257, WhatsNew.cs:305
- [new Font] `new Font(Ink.MenuFamily, 11.5f * s, GraphicsUnit.Pixel)` ×2 — PrompterControls.cs:119, ScheduleDialogPickers.cs:197
- [new Font] `new Font(Ink.MenuFamily, fs, FontStyle.Bold, GraphicsUnit.Pixel)` ×2 — RedactWindowModel.cs:288, RedactWindowSelect.cs:323
- [alpha-on-token] `Color.FromArgb(255, e.StrokeColor)` ×2 — RedactWindowModel.cs:181, RedactWindowModel.cs:324
- [alpha-on-token] `Color.FromArgb(68, e.StrokeColor)` ×2 — RedactWindowModel.cs:266, RedactWindowModel.cs:276
- [RGB] `Color.FromArgb(255, 0, 0, 0)` ×1 — KitButtons.cs:66
- [RGB] `Color.FromArgb(255, 22, 104, 232)` ×1 — KitButtons.cs:86
- [RGB] `Color.FromArgb(255, 142, 142, 147)` ×1 — KitPickers.cs:132
- … 79 pola lain (masing-masing lebih jarang)

### Combo*

File: 6 · RGB+hex 41 · named 3 · alpha-on-token 0 · new Font 0 · radius literal 20 / token 0 · token usages 23

- [RGB] `Color.FromArgb(255, 50, 173, 230)` ×6 — ComboFx.cs:71, ComboFx.cs:237, ComboFx.cs:661, ComboFx.cs:697, ComboFx.cs:698, ComboFx.cs:706
- [RGB] `Color.FromArgb(255, 255, 214, 10)` ×6 — ComboFx.cs:73, ComboFx.cs:82, ComboFx.cs:235, ComboFx.cs:568, ComboFx.cs:646, ComboInput.cs:352
- [radius] `FillRound(…, 1.2f` ×6 — ComboFx.cs:644, ComboFx.cs:646, ComboFx.cs:651, ComboFx.cs:653, ComboFx.cs:680, ComboFx.cs:681
- [RGB] `Color.FromArgb(255, 255, 80, 90)` ×4 — ComboFx.cs:570, ComboFx.cs:675, ComboFx.cs:676, ComboInput.cs:162
- [radius] `FillRound(…, 1.5f` ×4 — ComboFx.cs:705, ComboFx.cs:706, ComboFx.cs:766, ComboFx.cs:767
- [RGB] `Color.FromArgb(255, 255, 159, 10)` ×3 — ComboFx.cs:74, ComboFx.cs:85, ComboFx.cs:234
- [RGB] `Color.FromArgb(255, 255, 90, 95)` ×2 — ComboFx.cs:32, ComboFx.cs:614
- [RGB] `Color.FromArgb(255, 255, 70, 85)` ×2 — ComboFx.cs:468, ComboFx.cs:713
- [RGB] `Color.FromArgb(255, 255, 200, 70)` ×2 — ComboFx.cs:503, ComboInput.cs:389
- [RGB] `Color.FromArgb(255, 48, 209, 88)` ×2 — ComboFx.cs:72, ComboFx.cs:236
- [RGB] `Color.FromArgb(255, 255, 55, 95)` ×2 — ComboFx.cs:75, ComboFx.cs:233
- [RGB] `Color.FromArgb(255, 255, 248, 220)` ×2 — ComboFx.cs:77, ComboFx.cs:83
- [radius] `FillRound(…, 1f` ×2 — ComboFx.cs:513, ComboFx.cs:731
- [radius] `FillRound(…, fw * 0.5f` ×2 — ComboFx.cs:563, ComboFx.cs:564
- [named] `Color.White` ×2 — ComboFx.cs:590, ComboFx.cs:590
- [RGB] `Color.FromArgb(255, 255, 245, 200)` ×1 — ComboFx.cs:51
- [RGB] `Color.FromArgb(255, 90, 220, 255)` ×1 — ComboFx.cs:575
- [RGB] `Color.FromArgb(255, 16, 18, 22)` ×1 — ComboFx.cs:590
- [RGB] `Color.FromArgb(255, 175, 82, 222)` ×1 — ComboFx.cs:76
- [RGB] `Color.FromArgb(255, 255, 185, 40)` ×1 — ComboFx.cs:84
- [RGB] `Color.FromArgb(255, 255, 235, 130)` ×1 — ComboFx.cs:86
- [RGB] `Color.FromArgb(255, 255, 190, 60)` ×1 — ComboInput.cs:153
- [RGB] `Color.FromArgb(255, 255, 220, 70)` ×1 — ComboInput.cs:252
- [RGB] `Color.FromArgb(255, 255, 140, 40)` ×1 — ComboInput.cs:370
- [RGB] `Color.FromArgb(255, 255, 110, 40)` ×1 — ComboInput.cs:389
- … 7 pola lain (masing-masing lebih jarang)

### Other

File: 155 · RGB+hex 32 · named 32 · alpha-on-token 12 · new Font 5 · radius literal 63 / token 9 · token usages 356

- [named] `Color.White` ×19 — AlbumAccent.cs:32, ColorPicker.cs:193, Colors.cs:85, ConverterImage.cs:63, Glass.cs:748, Glass.cs:748, Glass.cs:780, Painter.cs:20, … (+11)
- [radius] `FillRound(…, 12` ×8 — Clips.cs:409, ControlWifi.cs:51, ControlWifi.cs:52, ControlWifi.cs:129, ControlWifi.cs:130, Suara.cs:224, Tabs.cs:421, Tabs.cs:557
- [named] `Color.Black` ×6 — AlbumAccent.cs:32, Clips.cs:389, ColorPicker.cs:49, GlassKind.cs:73, WallBackdrop.cs:161, WallSync.cs:228
- [named] `Color.Transparent` ×6 — CompGlass.cs:524, Mini.cs:479, Parts.cs:150, Parts.cs:340, Place.cs:174, Reminders.cs:652
- [radius] `FillRound(…, 2` ×5 — Clips.cs:413, Clips.cs:414, ConvertView.cs:269, ConvertView.cs:273, ConvertView.cs:280
- [radius] `FillRound(…, 1` ×5 — Downloads.cs:205, Downloads.cs:206, Handset.cs:323, Side.cs:114, Side.cs:115
- [radius] `FillRound(…, 1.5f` ×4 — Handset.cs:363, Handset.cs:364, Side.cs:147, Side.cs:149
- [radius] `FillRound(…, 14` ×4 — Music.cs:360, Music.cs:361, Suara.cs:169, Tabs.cs:418
- [RGB] `Color.FromArgb(255, 0, 0, 0)` ×2 — Colors.cs:72, Colors.cs:86
- [RGB] `Color.FromArgb(0, 255, 255, 255)` ×2 — Glass.cs:607, Glass.cs:767
- [RGB] `Color.FromArgb(255, 14, 14, 14)` ×2 — Music.cs:509, Music.cs:563
- [radius] `FillRound(…, 9` ×2 — Clips.cs:389, Clips.cs:393
- [radius] `FillRound(…, 13` ×2 — ControlWifi.cs:85, ControlWifi.cs:156
- [radius] `FillRound(…, 4.5f` ×2 — Dictate.cs:446, Rapat.cs:420
- [radius] `Ink.Round(…, 8 * ui` ×2 — Integrations.cs:53, Integrations.cs:56
- [radius] `FillRound(…, 5` ×2 — Parts.cs:460, Parts.cs:462
- [radius] `FillRound(…, 2.5f` ×2 — Reminders.cs:651, Reminders.cs:652
- [radius] `Ink.Round(…, 12` ×2 — Shelf.cs:300, Shelf.cs:317
- [alpha-on-token] `Color.FromArgb(255, p.T.Bg)` ×2 — Clips.cs:408, Clips.cs:421
- [alpha-on-token] `Color.FromArgb(255, c)` ×2 — ColorPicker.cs:96, Colors.cs:199
- [RGB] `Color.FromArgb(212, 184, 143)` ×1 — Clips.cs:412
- [RGB] `Color.FromArgb(146, 110, 72)` ×1 — Clips.cs:412
- [RGB] `Color.FromArgb(160, 0, 0, 0)` ×1 — ColorPicker.cs:191
- [hex] `ColorTranslator.FromHtml(` ×1 — Colors.cs:210
- [RGB] `Color.FromArgb(110, 128, 128, 128)` ×1 — Colors.cs:216
- … 63 pola lain (masing-masing lebih jarang)

## Peta permukaan (top-level window / form / dialog)

31 kelas turunan `Form` (transitif; partial digabung). Kolom "Grup" mengikuti pengelompokan di atas.

| Kelas | Rantai turunan | Deklarasi | Grup |
|---|---|---|---|
| AgentPromptBox | AgentPromptBox → KitWindow → Form | AgentPromptBox.cs:17 | Schedule* / Prompter* / SuruhChat* / Redact* / Tour / WhatsNew / Kit* / AgentPrompt* |
| PickerOverlay | PickerOverlay → Form | ColorPicker.cs:32 | Other |
| Loupe | Loupe → Form | ColorPicker.cs:125 | Other |
| WifiPassBox | WifiPassBox → KitWindow → Form | ControlWifiPass.cs:14 | Other |
| AppSheet | AppSheet → GlassSheet → Form | DockAppMenu.cs:13 | Dock* |
| CalendarSheet | CalendarSheet → GlassSheet → Form | DockCal.cs:12 | Dock* |
| DockCard | DockCard → Form | DockCard.cs:15 | Dock* |
| QuickSheet | QuickSheet → GlassSheet → Form | DockQuick.cs:37 | Dock* |
| GlassSheet | GlassSheet → Form | DockSheet.cs:15 | Dock* |
| StartCard | StartCard → GlassSheet → Form | DockStart.cs:52 | Dock* |
| WeatherSheet | WeatherSheet → GlassSheet → Form | DockWeather.cs:10 | Dock* |
| DockWindow | DockWindow → Form | DockWindow.cs:18 | Dock* |
| FeedbackWindow | FeedbackWindow → KitWindow → Form | Feedback.cs:63 | Other |
| Island | Island → Form | Island.cs:48 | Island* / Panel / Views / Overview |
| KitWindow | KitWindow → Form | KitWindow.cs:12 | Schedule* / Prompter* / SuruhChat* / Redact* / Tour / WhatsNew / Kit* / AgentPrompt* |
| KitAsk | KitAsk → KitWindow → Form | KitWindow.cs:216 | Schedule* / Prompter* / SuruhChat* / Redact* / Tour / WhatsNew / Kit* / AgentPrompt* |
| KuliaLogin | KuliaLogin → KitWindow → Form | Kulia.cs:339 | Other |
| NoteBox | NoteBox → KitWindow → Form | Notes.cs:67 | Work* / Task* / Note* |
| WordsWindow | WordsWindow → KitWindow → Form | PrefsDikte.cs:152 | Prefs* |
| PrefsWindow | PrefsWindow → KitWindow → Form | PrefsWindow.cs:13 | Prefs* |
| PriorityPromptDialog | PriorityPromptDialog → KitWindow → Form | Priorities.cs:327 | Other |
| PrompterSetup | PrompterSetup → KitWindow → Form | PrompterSetup.cs:12 | Schedule* / Prompter* / SuruhChat* / Redact* / Tour / WhatsNew / Kit* / AgentPrompt* |
| RedactWindow | RedactWindow → KitWindow → Form | RedactWindow.cs:13 (+partial: RedactWindowCrop.cs:13, RedactWindowInput.cs:13, RedactWindowModel.cs:13, RedactWindowSelect.cs:13, RedactWindowText.cs:13) | Schedule* / Prompter* / SuruhChat* / Redact* / Tour / WhatsNew / Kit* / AgentPrompt* |
| GoogleEventDialog | GoogleEventDialog → KitWindow → Form | ScheduleDialog.cs:13 | Schedule* / Prompter* / SuruhChat* / Redact* / Tour / WhatsNew / Kit* / AgentPrompt* |
| ChatWindow | ChatWindow → KitWindow → Form | SuruhChatWindow.cs:16 | Schedule* / Prompter* / SuruhChat* / Redact* / Tour / WhatsNew / Kit* / AgentPrompt* |
| TaskDragPreview | TaskDragPreview → Form | TaskDragPreview.cs:9 | Work* / Task* / Note* |
| TaskEditor | TaskEditor → KitWindow → Form | TaskEditor.cs:8 | Work* / Task* / Note* |
| TourCard | TourCard → KitWindow → Form | Tour.cs:98 | Schedule* / Prompter* / SuruhChat* / Redact* / Tour / WhatsNew / Kit* / AgentPrompt* |
| WhatsNewWindow | WhatsNewWindow → KitWindow → Form | WhatsNew.cs:159 | Schedule* / Prompter* / SuruhChat* / Redact* / Tour / WhatsNew / Kit* / AgentPrompt* |
| WorkWindow | WorkWindow → KitWindow → Form | WorkWindow.cs:9 (+partial: WorkWindowNotes.cs:10, WorkWindowTasks.cs:9) | Work* / Task* / Note* |
| ListNameDialog | ListNameDialog → KitWindow → Form | WorkWindowTasks.cs:678 | Work* / Task* / Note* |

Permukaan non-Form terkait (tidak dihitung di atas):

- `Cue` : NativeWindow — AgentPromptUi.cs:17
- `CompBackdrop` : NativeWindow — CompGlass.cs:286
- `Keeper` : NativeWindow — DockKeys.cs:121
- `NoteBlockMenu` : ToolStripDropDown — NoteBlockMenu.cs:8
- `MenuLook` : ToolStripProfessionalRenderer — Menus.cs:183 (renderer semua `ContextMenuStrip`; dipasang Menus.cs:327, NoteBlockMenu.cs:52). `new ContextMenuStrip()` dibuat ad-hoc di ±20 lokasi (Awake, Colors, ControlBluetooth, ConvertPage, DockCal, DockInput, DockInputMenu, DockQuick, DockStart, IslandSystem, KitButtons, LyricOffset, NotifAppRules, Overview, PrefsPages, Priorities).
- Dialog sistem (tidak di-theme): `OpenFileDialog/SaveFileDialog/ColorDialog/FolderBrowserDialog` di Colors.cs:230, ConvertPage.cs:88, Handset.cs:250, PrefsIbadah.cs:62, PrefsPages.cs:67, PrefsPages.cs:79, PrompterSetup.cs:308, RedactWindowModel.cs:485, Shelf.cs:326, TaskNoteEditor.cs:576; `MessageBox.Show` di Program.cs:50.
- `Island` (Island.cs:48) adalah satu Form dengan banyak "view" partial (Prompter, Combo, Overview, Panel, Views, Mini, Mirror, EdgeLight, Shelf, Side, Eyes, Rapat, Meeting, Shots, Mixer, Clips, Glass, …) — permukaan logis, bukan window terpisah.

## Warna literal paling umum (kandidat token)

Dikelompokkan per RGB (alpha diabaikan untuk pengelompokan; daftar alpha yang dipakai ditampilkan). Hanya di luar token source.

| # | RGB | Hex | Jumlah | Alpha dipakai | Lokasi |
|---|---|---|---|---|---|
| 1 | 255, 255, 255 | #FFFFFF | 28 | 0, 30, 34, 38, 46, 50, 60, 96, 105, 110, 120, 150, 185, 236, 255, * | Colors.cs:71, Glass.cs:607, Glass.cs:607, Glass.cs:766, Glass.cs:767, Glass.cs:807, Music.cs:511, Music.cs:512, Music.cs:517, Painter.cs:202, … (+18) |
| 2 | 0, 0, 0 | #000000 | 21 | 1, 22, 30, 36, 65, 120, 130, 140, 150, 160, 255, * | ColorPicker.cs:191, Colors.cs:72, Colors.cs:86, DockPaint.cs:115, DockPaint.cs:124, Glass.cs:696, Glass.cs:725, Glass.cs:776, IslandRender.cs:411, KitButtons.cs:66, … (+11) |
| 3 | 50, 173, 230 | #32ADE6 | 7 | 255 | ComboFx.cs:71, ComboFx.cs:237, ComboFx.cs:661, ComboFx.cs:697, ComboFx.cs:698, ComboFx.cs:706, EdgeLight.cs:228 |
| 4 | 255, 214, 10 | #FFD60A | 7 | 255 | ComboFx.cs:73, ComboFx.cs:82, ComboFx.cs:235, ComboFx.cs:568, ComboFx.cs:646, ComboInput.cs:352, EdgeLight.cs:228 |
| 5 | 255, 80, 90 | #FF505A | 4 | 255 | ComboFx.cs:570, ComboFx.cs:675, ComboFx.cs:676, ComboInput.cs:162 |
| 6 | 48, 209, 88 | #30D158 | 3 | 255 | ComboFx.cs:72, ComboFx.cs:236, EdgeLight.cs:228 |
| 7 | 255, 55, 95 | #FF375F | 3 | 255 | ComboFx.cs:75, ComboFx.cs:233, EdgeLight.cs:228 |
| 8 | 255, 159, 10 | #FF9F0A | 3 | 255 | ComboFx.cs:74, ComboFx.cs:85, ComboFx.cs:234 |
| 9 | 14, 14, 14 | #0E0E0E | 2 | 255 | Music.cs:509, Music.cs:563 |
| 10 | 14, 165, 233 | #0EA5E9 | 2 | 255 | NoteControls.cs:18, RedactWindowModel.cs:389 |
| 11 | 16, 185, 129 | #10B981 | 2 | 255 | NoteControls.cs:17, RedactWindowModel.cs:387 |
| 12 | 55, 60, 68 | #373C44 | 2 | 90, 110 | PrefsThemePicker.cs:286, PrefsThemePicker.cs:321 |
| 13 | 59, 130, 246 | #3B82F6 | 2 | 255 | KitButtons.cs:86, RedactWindowModel.cs:377 |
| 14 | 139, 92, 246 | #8B5CF6 | 2 | 255 | NoteControls.cs:19, RedactWindowModel.cs:384 |
| 15 | 255, 70, 85 | #FF4655 | 2 | 255 | ComboFx.cs:468, ComboFx.cs:713 |
| 16 | 255, 90, 95 | #FF5A5F | 2 | 255 | ComboFx.cs:32, ComboFx.cs:614 |
| 17 | 255, 200, 70 | #FFC846 | 2 | 255 | ComboFx.cs:503, ComboInput.cs:389 |
| 18 | 255, 248, 220 | #FFF8DC | 2 | 255 | ComboFx.cs:77, ComboFx.cs:83 |
| 19 | 6, 182, 212 | #06B6D4 | 1 | 255 | RedactWindowModel.cs:379 |
| 20 | 8, 9, 11 | #08090B | 1 | 238 | PrefsThemePicker.cs:262 |
| 21 | 13, 15, 19 | #0D0F13 | 1 | 255 | RedactWindowModel.cs:394 |
| 22 | 14, 16, 20 | #0E1014 | 1 | 255 | PrefsThemePicker.cs:22 |
| 23 | 16, 16, 20 | #101014 | 1 | 205 | RedactWindow.cs:799 |
| 24 | 16, 18, 22 | #101216 | 1 | 255 | ComboFx.cs:590 |
| 25 | 18, 18, 20 | #121214 | 1 | 255 | SuruhChat.cs:268 |
| 26 | 18, 18, 22 | #121216 | 1 | 255 | RedactWindowModel.cs:348 |
| 27 | 20, 22, 27 | #14161B | 1 | 235 | KitPickers.cs:159 |
| 28 | 20, 184, 166 | #14B8A6 | 1 | 255 | RedactWindowModel.cs:388 |
| 29 | 21, 24, 30 | #15181E | 1 | 255 | RedactWindowModel.cs:393 |
| 30 | 22, 24, 29 | #16181D | 1 | 175 | PrefsThemePicker.cs:316 |
| 31 | 22, 26, 34 | #161A22 | 1 | 72 | PrefsThemePicker.cs:28 |
| 32 | 22, 104, 232 | #1668E8 | 1 | 255 | KitButtons.cs:86 |
| 33 | 24, 24, 30 | #18181E | 1 | 220 | RedactWindow.cs:840 |
| 34 | 24, 28, 34 | #181C22 | 1 | 24 | PrefsThemePicker.cs:304 |
| 35 | 26, 26, 30 | #1A1A1E | 1 | 255 | RedactWindow.cs:638 |
| 36 | 28, 31, 37 | #1C1F25 | 1 | 255 | PrefsThemePicker.cs:21 |
| 37 | 32, 36, 44 | #20242C | 1 | 255 | RedactWindowModel.cs:392 |
| 38 | 34, 34, 40 | #222228 | 1 | 255 | RedactWindow.cs:639 |
| 39 | 40, 44, 51 | #282C33 | 1 | 228 | PrefsThemePicker.cs:25 |
| 40 | 48, 102, 205 | #3066CD | 1 | 255 | PrefsThemePicker.cs:523 |

Total 71 RGB berbeda; 53 hanya muncul sekali.

### Warna bernama

| Warna | Jumlah | Contoh lokasi |
|---|---|---|
| `Color.White` | 42 | AlbumAccent.cs:32, ColorPicker.cs:193, Colors.cs:85, ComboFx.cs:590, ComboFx.cs:590, ConverterImage.cs:63, DockCard.cs:590, DockPaintSpan.cs:204 … |
| `Color.Transparent` | 25 | CompGlass.cs:524, DockCard.cs:405, DockPaint.cs:45, DockPaintSpan.cs:60, DockSheet.cs:476, DockSheet.cs:490, DockSheet.cs:500, DockSheet.cs:515 … |
| `Color.Black` | 15 | AlbumAccent.cs:32, Clips.cs:389, ColorPicker.cs:49, ComboFx.cs:590, GlassKind.cs:73, Overview.cs:649, Overview.cs:783, PrefsThemePicker.cs:234 … |
| `Color.Peru` | 1 | Clips.cs:392 |

### Hex string

| Literal | Jumlah | Lokasi |
|---|---|---|
| `ColorTranslator.FromHtml(` | 3 | Colors.cs:210, KitPickers.cs:136, RedactWindow.cs:154 |
| `"#FFFFFF"` | 3 | PrefsWindow.cs:792, PrefsWindow.cs:793, RedactWindow.cs:50 |
| `"#4C8DFF"` | 2 | Prefs.cs:220, RedactWindow.cs:49 |
| `"#A78BFA"` | 2 | Prefs.cs:221, RedactWindow.cs:50 |
| `"#FF9F43"` | 2 | Prefs.cs:224, RedactWindow.cs:50 |
| `"#FFD24A"` | 2 | Prefs.cs:225, RedactWindow.cs:49 |
| `"#3DD68C"` | 2 | Prefs.cs:226, RedactWindow.cs:49 |
| `"#111111"` | 2 | PrefsWindow.cs:793, RedactWindow.cs:50 |
| `"#FF3B30"` | 2 | RedactWindow.cs:49, RedactWindow.cs:154 |
| `"#22c55e"` | 1 | GoogleCal.cs:375 |
| `"#ef4444"` | 1 | GoogleCal.cs:375 |
| `"#FF6B9A"` | 1 | Prefs.cs:222 |
| `"#FF5252"` | 1 | Prefs.cs:223 |
| `"#1668E8"` | 1 | Prefs.cs:232 |
| `"#6C3CE0"` | 1 | Prefs.cs:233 |
| `"#C2185B"` | 1 | Prefs.cs:234 |
| `"#C5221F"` | 1 | Prefs.cs:235 |
| `"#A84B00"` | 1 | Prefs.cs:236 |
| `"#855E00"` | 1 | Prefs.cs:237 |
| `"#107C41"` | 1 | Prefs.cs:238 |
| `"#000000"` | 1 | PrefsWindow.cs:792 |
| `"#1C1C1E"` | 1 | PrefsWindow.cs:792 |
| `"#0E1734"` | 1 | PrefsWindow.cs:792 |
| `"#0F2A22"` | 1 | PrefsWindow.cs:792 |
| `"#F5EFE6"` | 1 | PrefsWindow.cs:792 |
| `"#EEF4FF"` | 1 | PrefsWindow.cs:793 |
| `"#3B2F2F"` | 1 | PrefsWindow.cs:793 |

## Font

`new Font(` di luar `Ink.Get`: 54; string family di luar `Ink.Init`: 11; helper font Ink: 614; `UiFont(`: 67. Sebagian besar `new Font` memakai `Ink.MenuFamily`/`Ink.IconFamily` (family dari token) tetapi ukuran literal dan tidak di-cache (alokasi per paint).

- **NoteEditor.cs** (8): 227: `new Font(Font.FontFamily, Font.Size * 1.52f, FontStyle.Bold)`; 228: `new Font(Font.FontFamily, Font.Size * 1.3f, FontStyle.Bold)`; 229: `new Font(Font.FontFamily, Font.Size * 1.12f, FontStyle.Bold)`; 230: `new Font(Font, FontStyle.Bold)`; 231: `new Font(Font, FontStyle.Italic)`; 232: `new Font(Font, FontStyle.Bold | FontStyle.Italic)`; 233: `new Font(Font, FontStyle.Strikeout)`; 234: `new Font(FontFamily.GenericMonospace, Font.Size)`
- **WhatsNew.cs** (6): 266: `new Font(Ink.MenuFamily, 13.5f * s, GraphicsUnit.Pixel)`; 267: `new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel)`; 301: `new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel)`; 303: `new Font(Ink.MenuFamily, 13.5f * s, GraphicsUnit.Pixel)`; 304: `new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel)`; 305: `new Font(Ink.IconFamily, 15f * s, GraphicsUnit.Pixel)`
- **PrefsWindow.cs** (5): 308: `new Font(Ink.IconFamily, 13f * Ui, GraphicsUnit.Pixel)`; 314: `new Font(Ink.MenuFamily, 14f * Ui, GraphicsUnit.Pixel)`; 458: `new Font(Ink.IconFamily, 22f * s, GraphicsUnit.Pixel)`; 459: `new Font(Ink.MenuFamily, 24f * s, FontStyle.Bold, GraphicsUnit.Pixel)`; 465: `new Font(Ink.MenuFamily, 13f * s, GraphicsUnit.Pixel)`
- **ScheduleDialogPickers.cs** (5): 33: `new Font(Ink.MenuFamily, 13f * s, FontStyle.Bold, GraphicsUnit.Pixel)`; 34: `new Font(Ink.MenuFamily, 11f * s, GraphicsUnit.Pixel)`; 35: `new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel)`; 196: `new Font(Ink.MenuFamily, 12f * s, FontStyle.Bold, GraphicsUnit.Pixel)`; 197: `new Font(Ink.MenuFamily, 11.5f * s, GraphicsUnit.Pixel)`
- **KitButtons.cs** (4): 234: `new Font(Ink.MenuFamily, 13.5f * s, GraphicsUnit.Pixel)`; 235: `new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel)`; 257: `new Font(Ink.IconFamily, 15f * s, GraphicsUnit.Pixel)`; 265: `new Font(Ink.MenuFamily, 9.5f * s, GraphicsUnit.Pixel)`
- **Menus.cs** (3): 195: `new Font(Ink.MenuFamily, 9.5f)`; 196: `new Font(Ink.MenuFamily, 9.5f, FontStyle.Bold)`; 197: `new Font(Ink.IconFamily, 12.5f)`
- **PrefsSideList.cs** (3): 130: `new Font(Ink.MenuFamily, 13.5f * s, GraphicsUnit.Pixel)`; 131: `new Font(Ink.MenuFamily, 11.5f * s, FontStyle.Bold, GraphicsUnit.Pixel)`; 132: `new Font(Ink.IconFamily, 14f * s, GraphicsUnit.Pixel)`
- **PrompterControls.cs** (3): 38: `new Font(Ink.MenuFamily, 14f * ui, GraphicsUnit.Pixel)`; 118: `new Font(Ink.MenuFamily, 13f * s, GraphicsUnit.Pixel)`; 119: `new Font(Ink.MenuFamily, 11.5f * s, GraphicsUnit.Pixel)`
- **DockApps.cs** (2): 223: `"Microsoft.Windows.Explorer"`; 393: `"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"`
- **Kit.cs** (2): 286: `new Font(Ink.MenuFamily, 12.5f * Ui, FontStyle.Regular, GraphicsUnit.Pixel)`; 287: `new Font(Ink.MenuFamily, 12.5f * Ui, FontStyle.Regular, GraphicsUnit.Pixel)`
- **KitWindow.cs** (2): 187: `new Font(Ink.MenuFamily, px * Ui, GraphicsUnit.Pixel)`; 205: `new Font(parent.Font.FontFamily, parent.Font.Size * s, parent.Font.Style, parent.Font.Unit)`
- **Painter.cs** (2): 248: `new Font("Segoe UI Emoji", f.Size, FontStyle.Regular, f.Unit)`; 248: `"Segoe UI Emoji"`
- **RedactWindowModel.cs** (2): 288: `new Font(Ink.MenuFamily, fs, FontStyle.Bold, GraphicsUnit.Pixel)`; 329: `new Font(Ink.MenuFamily, Math.Max(8f, fs), FontStyle.Bold, GraphicsUnit.Pixel)`
- **Converter.cs** (1): 285: `"Microsoft\WinGet\Links"`
- **DockAppMenu.cs** (1): 170: `"Microsoft.Windows.Explorer"`
- **DockKeys.cs** (1): 881: `"Microsoft"`
- **Integrations.cs** (1): 24: `new Font(Ink.MenuFamily, 12.5f * ui, GraphicsUnit.Pixel)`
- **KitPickers.cs** (1): 233: `new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel)`
- **NotifOnce.cs** (1): 155: `"Microsoft.ScreenSketch"`
- **Notifs.cs** (1): 35: `"Microsoft\Windows\Notifications"`
- **PrefsCitySuggest.cs** (1): 160: `new Font(Ink.MenuFamily, 12.5f * ui, GraphicsUnit.Pixel)`
- **PrefsDikte.cs** (1): 231: `new Font(Ink.MenuFamily, 13f * Ui, GraphicsUnit.Pixel)`
- **PrefsTabs.cs** (1): 57: `new Font(Ink.IconFamily, 12f * s, GraphicsUnit.Pixel)`
- **RedactToolStrip.cs** (1): 285: `new Font(Ink.MenuFamily, Math.Max(8f, u * 0.56f), FontStyle.Bold, GraphicsUnit.Pixel)`
- **RedactWindowSelect.cs** (1): 323: `new Font(Ink.MenuFamily, fs, FontStyle.Bold, GraphicsUnit.Pixel)`
- **RedactWindowText.cs** (1): 63: `new Font(Ink.MenuFamily, scrFs, FontStyle.Bold, GraphicsUnit.Pixel)`
- **ScheduleDialog.cs** (1): 477: `new Font(Ink.MenuFamily, 18f * s, FontStyle.Bold, GraphicsUnit.Pixel)`
- **TaskNoteEditor.cs** (1): 24: `"Segoe UI"`
- **TaskNoteMarkdown.cs** (1): 15: `"Consolas"`
- **TaskView.cs** (1): 320: `new Font(f, FontStyle.Strikeout)`
- **WallSync.cs** (1): 132: `"Microsoft\Windows\Themes\TranscodedWallpaper"`

## Token source (dikecualikan dari hitungan)

- `src/Ink.cs` — `class Tone` (12–88): `Dark()` 40–50 (Bg 32,32,32; Text 255; Sub α158; Faint α148; Fill α18; FillHover α28; Line α23 — putih), `Defaults()` 59–60 (Positive/Negative dark+light), `Derive()` 71–72 (Highlight, Lip), `Paper()` 79–85 (Bg 243; Text/Sub/Faint 26,26,26; Fill/FillHover/Line hitam α14/24/24); `OnGlass()` 32 (alpha min 228).
- `src/Ink.cs` — accent preset hex `DarkAccents` 110–120, `LightAccents` 122–132; pemetaan `GetLightAccent/GetDarkAccent` 136–158; ink on-accent 214–215 (10,10,10 / 250,250,250).
- `src/Ink.cs` — skala: `Sp4…Sp32` 248, `RadiusShell/Module/Control` 250, `ModuleInset/Gap/Pad` 252; font family 267–273; type scale 295–301.
- `src/Kit.cs` — `KitInk` 12–36 (Window, Side, Group, Line, Hover, Raised, Text, Sub, Faint, Disabled, Accent, OnAccent; turunan `Ink.Mix` dari `Ink.Base`, memakai `Color.White/Black` sebagai mix target).
- Literal di region token source yang dikecualikan: 53 kemunculan.
- Palet sekunder yang berperilaku seperti token tapi berada di luar Ink/Kit (tercatat sebagai pelanggar di atas): lihat `PrefsThemePicker.cs:21–28` (swatch preview tema), `NoteControls.cs:16–20` (warna label catatan), `ComboFx.cs:71–77` (palet confetti).
