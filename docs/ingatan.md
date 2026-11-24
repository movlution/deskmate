# Ingatan

[← Kembali ke README](../README.md)

Ingatan adalah memori aktivitas lokal di Deskmate, mirip PiecesOS dengan Long-Term Memory. Deskmate mencatat aplikasi, jendela, halaman, teks di layar, teks yang kamu salin, dan transkrip rapat. Setelah itu kamu bisa bertanya, misalnya *"tadi pagi saya kerja apa?"* atau *"docs apa yang saya baca soal WASAPI kemarin?"*. Semua data disimpan di laptop ini. Ingatan mati sampai kamu menyalakannya di **Pengaturan › Ingatan**.

## Cara kerja

```
Deskmate.exe (island, dock, pet)  ──┐
Deskmate.exe --mcp (stdio)        ──┼──► API lokal 127.0.0.1 + token ──► Deskmate.exe --ingat
                                    │                                    ├─ Capture: jendela aktif, UI Automation, OCR cadangan
                                    │                                    ├─ Saringan privasi → episode
                                    │                                    ├─ SQLite FTS5 + vektor (ingat.db)
                                    │                                    ├─ Worker: ringkasan, embedding, rekap harian, fakta
                                    │                                    └─ Pencarian hibrida + jawaban
```

- **Proses sendiri.** `--ingat` berjalan sebagai proses terpisah berprioritas rendah. Deskmate menyalakannya saat Ingatan aktif dan menyalakannya lagi kalau berhenti mendadak (paling banyak tiga kali per jam). Proses ini berhenti sendiri saat Deskmate ditutup atau Ingatan dimatikan.
- **Dipicu event.** Pergantian jendela dan judul (tab browser, file di editor) memicu pembacaan. Pergantian yang tidak bertahan 1,5 detik diabaikan. Selama kamu di jendela yang sama, Deskmate mengambil sidik gambar kecil (dHash) tiap 5 detik. Teks dibaca ulang hanya kalau gambarnya berubah cukup jauh atau sudah lewat 30 detik.
- **Teks dulu, gambar belakangan.** Teks diambil lewat UI Automation. OCR bawaan Windows hanya dipakai kalau aplikasinya tidak memberi teks, dan dilewati saat baterai di bawah 25% atau saat game/presentasi memenuhi layar. Gambar layar tidak pernah disimpan.
- **Episode.** Aktivitas di aplikasi yang sama dan proyek yang sama (ditebak dari judul VS Code, JetBrains, Unity, terminal) atau situs yang sama digabung menjadi satu episode, selama jedanya di bawah 2 menit. Tidak ada input selama 3 menit menutup episode. Episode di bawah 20 detik dibuang.
- **Memori bertingkat.** Detail mentah (14 hari) → episode beserta ringkasannya (180 hari) → rekap harian dan fakta jangka panjang (tanpa batas). Batasnya bisa diatur.

## Bertanya

- **Chat Suruh:** tanya seperti biasa. Model memakai seksi `activity` dari alat `read_deskmate`.
- **Pet sebagai asisten:** klik pet lalu pilih **Tanya** (tombol pertama di bar aksinya). Ketik pertanyaan apa saja di kotak di atas pet, lalu tekan Enter. Pet berpikir ("Hmm, sebentar…") dan menjawab di gelembungnya. Klik jawabannya untuk bertanya lanjutan; pet mengingat percakapan selama 10 menit. Dengan Ingatan aktif, jawabannya memakai aktivitasmu lewat AI pilihan Ingatan. Kalau AI itu belum siap, atau Ingatan mati, pet tetap menjawab lewat AI utama (**Pengaturan › AI**) tanpa membawa data aktivitas. Pet juga memberi rekap pagi tentang kemarin dan mengingatkan istirahat setelah 2 jam di satu proyek; klik gelembungnya untuk langsung bertanya.
- **Asisten AI lain (MCP):** di **Pengaturan › Ingatan › Konfigurasi MCP**, klik **Salin**, lalu tempel ke konfigurasi klien MCP:

```json
{
  "mcpServers": {
    "deskmate": { "command": "C:\\path\\Deskmate.exe", "args": ["--mcp"] }
  }
}
```

  Untuk Claude Code:

```powershell
claude mcp add deskmate -- "C:\path\Deskmate.exe" --mcp
```

  Tool yang tersedia: `search_activity`, `ask_memory`, `get_timeline`, `current_context`, `get_episode`, `list_meetings`, `get_meeting_transcript`, `get_facts`, `list_people`, `get_person`, `search_messages`, `get_promises`. Semuanya hanya membaca.

Pencarian menggabungkan BM25 (SQLite FTS5) dengan kemiripan makna (embedding) memakai *reciprocal rank fusion*. Kata waktu seperti "tadi pagi", "kemarin", "minggu lalu", "3 jam terakhir", atau "yesterday afternoon" otomatis menjadi rentang waktu.

## Percakapan dan orang

Ingatan mengenali aplikasi chat dan email: WhatsApp, Telegram, Discord, Slack, Teams, Signal, Messenger, LINE, Outlook, Thunderbird, serta versi web-nya (WhatsApp Web, Gmail, Outlook web, Slack, Discord, dan pesan langsung di X, Instagram, LinkedIn).

- **Yang dicatat:** isi obrolan yang terlihat di layar, draf yang sedang kamu tulis, dan pesan yang kamu kirim. Kalau judul jendela menyebut nama lawan bicara (Discord, Telegram, Teams), tiap percakapan menjadi episode sendiri.
- **Dipilah AI:** setelah episode chat selesai, AI memilah isinya menjadi percakapan (orang, grup, kanal, atau utas email), pesan per pengirim, ringkasan, dan **janji**. Janji mencakup apa yang kamu janjikan dan apa yang diminta orang lain darimu, termasuk tenggatnya.
- **Satu orang lintas aplikasi:** orang yang sama di WhatsApp, Discord, dan email digabung lewat namanya, tanpa membedakan huruf besar, gelar (Pak, Bu, Mas, Kak), atau emoji. AI juga mencatat siapa dia bagimu kalau itu jelas dari obrolan (klien, teman kuliah, atasan).
- **Bertanya tentang orang:** pertanyaan yang menyebut nama ("Budi minta apa ke aku?", "apa janjiku ke Rina?") otomatis membawa obrolan, pesan terakhir, dan janji dengan orang itu ke pet, Suruh, dan MCP.

## Isi kolom yang kamu tulis

Ingatan membaca isi kolom yang sedang fokus langsung dari kolomnya lewat UI Automation, bukan dari keyboard. Ketikan, tombol, dan pintasan tidak pernah direkam.

- Draf disimpan setelah isinya stabil beberapa detik.
- Kalau kolom yang sama kosong di tab atau obrolan yang sama, isinya dicatat sebagai pesan terkirim (di chat) atau teks yang ditulis (di aplikasi lain). Pindah tab, pindah obrolan, atau pindah kolom tidak dihitung.
- Saat kamu mengetik di chat, pengecekan dipercepat menjadi tiap 1,5 detik supaya pesan terkirim tercatat utuh.
- Kolom kata sandi, kotak pencarian, dan bilah alamat dilewati. Dokumen panjang (Notepad, Word) tidak dianggap draf, karena isinya sudah tercatat sebagai teks layar.

## Konteks saat menulis pesan

Saat kamu mulai mengetik balasan di chat atau email, pet menampilkan gelembung singkat: siapa orang itu bagimu, apa yang terakhir dibahas, janji yang masih terbuka, dan hal yang relevan dengan drafmu. Contoh: "Budi Santoso meminta revisi harga paket animasi menjadi 7,5 juta. Pastikan portofolio game edukasi ikut dilampirkan." Gelembung ini muncul paling sering sekali per percakapan tiap 30 menit, dan tidak muncul kalau tidak ada yang berguna untuk disampaikan. Klik gelembungnya untuk bertanya lanjut ke pet. Matikan di **Pengaturan › Ingatan › Konteks saat menulis pesan**.

## AI

Bawaannya **Ollama lokal**: ringkasan, rekap, dan jawaban tidak keluar dari laptop. Model embedding bawaannya `nomic-embed-text`. Siapkan dengan:

```powershell
ollama pull nomic-embed-text
```

Layanan cloud (Claude, OpenAI, dan lainnya) bisa dipilih di **Pengaturan › Ingatan › Layanan AI**. Deskmate meminta konfirmasi dulu, karena teks layar yang sudah disaring akan ikut terkirim. Kalau AI-nya **Mati** atau belum siap, pencarian kata kunci tetap berjalan, dan pertanyaan dijawab dengan daftar momen yang paling cocok.

Tidak semua server menyediakan embedding (endpoint `/embeddings` dengan model yang dipilih). Kalau permintaan embedding gagal, Ingatan berhenti mencobanya selama 6 jam dan mencari dengan kata kunci saja, jadi pertanyaan tidak tertahan menunggu batas waktu. Ringkasan, rekap, dan jawaban tetap memakai layanan teks yang sama. Untuk menghindari percobaan itu sama sekali, pilih **Model makna › Mati**.

Jendela Deskmate sendiri (pet, island, Pengaturan) tidak dicatat dan tidak memutus episode yang sedang berjalan. Episode diringkas setelah selesai dan kalau panjangnya minimal 1 menit; yang lebih pendek tetap bisa dicari lewat judul dan teksnya.

## Rapat

Kalau **Rekam panggilan otomatis** dinyalakan, Deskmate mulai merekam setelah mikrofon dipakai panggilan selama 20 detik, dan berhenti 15 detik setelah panggilan selesai. Perekamannya memakai fitur Rapat yang sudah ada: mikrofonmu dan suara speaker ditranskrip oleh mesin suara pilihanmu (whisper.cpp offline atau layanan cloud). Audio sementara dihapus setelah transkrip jadi. Island menampilkan "Rapat direkam" saat perekaman mulai. Beri tahu peserta lain. Transkrip `.md` di `%APPDATA%\Deskmate\rapat` diindeks Ingatan sebagai episode rapat.

## Privasi

- **Pesan orang lain:** isi obrolan yang terlihat di layar ikut tersimpan, termasuk pesan dari orang lain. Semuanya tetap di laptop ini, dan dihapus mengikuti lama simpan episode. Matikan **Percakapan dan orang** kalau tidak mau, atau tambahkan aplikasi chat tertentu ke **Aplikasi dikecualikan**.
- **Tidak pernah dicatat:** ketukan keyboard, jendela InPrivate/Incognito, kolom kata sandi (UI Automation `IsPassword`), pengelola kata sandi (1Password, Bitwarden, KeePass, dan lainnya), dompet kripto, layar masuk dan kata sandi Windows, judul jendela berisi kata seperti "password", "login", atau "OTP", situs perbankan umum, serta isi clipboard yang ditandai rahasia.
- **Disaring sebelum disimpan:** kunci API dan token (OpenAI, Anthropic, GitHub, Google, AWS, Slack, JWT, Bearer), kunci privat, nilai `password=`, nomor kartu, email, nomor HP, dan NIK. Query dan fragmen URL dibuang.
- **Pengecualian buatanmu:** aplikasi dan domain di **Pengaturan › Ingatan › Privasi**.
- **Kendali:** **Jeda 1 jam**, **Lupakan 1 jam terakhir**, **Hapus semua Ingatan**, atau matikan Ingatan.
- **API lokal** hanya mendengarkan di `127.0.0.1` dengan token acak di `%APPDATA%\Deskmate\ingat\api.json`. Request dari browser (ber-header `Origin`) ditolak.

## Berkas

| Berkas | Isi |
|---|---|
| `%APPDATA%\Deskmate\ingat\ingat.db` | Database SQLite (WAL): `episodes`, `events`, `episodes_fts`, `embeddings`, `daily`, `facts`, `meetings` |
| `%APPDATA%\Deskmate\ingat\api.json` | Port dan token API lokal selama layanan berjalan |

## API lokal

Semua request wajib membawa header `X-Deskmate-Token`.

| Metode | Jalur | Isi |
|---|---|---|
| GET | `/status` | Jumlah episode, ukuran database, status, dan provider |
| GET | `/now` | Episode yang sedang berjalan dan lama `streakMinutes` |
| GET | `/search?q=&from=&to=&limit=&text=1` | Pencarian hibrida |
| GET | `/timeline?date=yyyy-MM-dd` | Episode sehari dan rekapnya |
| GET | `/episode?id=` | Detail satu episode beserta teksnya |
| GET | `/meetings?days=` dan `/meeting?id=` | Daftar rapat dan transkripnya |
| GET | `/facts` | Fakta jangka panjang |
| GET | `/people?limit=` | Orang yang kamu ajak bicara, terbaru dulu |
| GET | `/person?name=` atau `?id=` | Satu orang: percakapan lintas aplikasi, pesan terakhir, dan janji |
| GET | `/messages?q=&from=&to=` | Cari pesan di semua chat dan email |
| GET | `/promises?person=&all=1` | Janji yang masih terbuka (atau semuanya) |
| POST | `/promise` `{"id": 1, "done": true}` | Menandai janji selesai |
| GET | `/composing` | Kolom chat yang sedang kamu tulis (aplikasi, percakapan, panjang draf) |
| GET | `/brief` | Ringkasan konteks untuk pesan yang sedang ditulis |
| POST | `/ask` `{"q": "..."}` | Jawaban beserta momen sumbernya |
| POST | `/pause` `{"minutes": 60}` | Jeda (`0` untuk lanjut) |
| POST | `/event` `{"source": "...", "text": "..."}` | Menambahkan teks ke episode yang sedang berjalan |
| POST | `/forget` `{"from": ISO, "to": ISO}` atau `{"all": true}` | Menghapus rentang waktu atau semuanya |
