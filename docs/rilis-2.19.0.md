# Deskmate 2.19.0

Rilis 26 November 2026. Kuota AI kini dibaca langsung dari akun yang sudah masuk.

## Baru

- **Kuota AI langsung dari akun.** Setiap 5 menit Deskmate membaca kuota dari akun yang sudah masuk, tanpa menunggu sesi agent berjalan: Claude Code per profil dari login-nya, Codex dari berkas `auth.json`, dan Antigravity dari language server yang sedang berjalan (kuota Gemini dan model lain). Copilot tetap dibaca dari log sesinya.
- **Status Terhubung.** AI yang sudah masuk tapi kuotanya tidak terbaca (misalnya Antigravity sedang ditutup, token Claude kedaluwarsa, atau Codex memakai kunci API) tetap tampil sebagai **Terhubung** beserta paketnya. AI yang belum masuk tidak tampil, dan barisnya hilang saat kamu keluar.

## Privasi

- Token tidak pernah dicatat di log. Saat layanan membatasi permintaan atau sedang gangguan, Deskmate menunggu 20 menit sebelum mencoba lagi.
