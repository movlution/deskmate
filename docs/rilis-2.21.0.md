# Deskmate 2.21.0

## Peningkatan Algoritma & Arsitektur Ingatan

- **Ekstraksi konteks relevan & chunk-grounded.** `Ask` dan `Chat` kini menarik potongan teks dengan kepadatan kata kunci tertinggi (`RelevantExcerpt`) atau langsung melompat ke window chunk pemenang dari pencarian semantik (`BestChunk`), bukan lagi memotong awal teks episode.
- **Pencarian semantik untuk fakta & percakapan.** Menambahkan tabel `item_vectors` dengan cache RAM `int8` terkuantisasi untuk fakta jangka panjang dan ringkasan percakapan, menggunakan satu embedding kueri bersama di seluruh pencarian episode, fakta, dan percakapan.
- **Batch Embedding API & `DotQ8` int8 unrolled.** Semua chunk episode, fakta, dan percakapan di-embed dalam satu permintaan HTTP batch (`Ai.EmbedBatch`) dengan fallback otomatis untuk endpoint non-batch; perhitungan cosine similarity kini menggunakan integer MAC `int8 × int8` 4-way unrolled.
- **Graf entitas ringan & pengelompokan workstream.** Skema DB v4 menambahkan tabel `episode_entities` + indeks `episode_entities_kv` (lengkap dengan backfill bertahap untuk data lama), memberi boost di RRF hybrid search, serta terekspos di endpoint `GET /entity` dan tool MCP `get_entity_graph`. Timeline kini mengelompokkan aktivitas lintas aplikasi yang berkaitan ke dalam `workstreams`.
- **Stemmer Indonesia & Inggris lebih luas.** Mendukung peluluhan awalan nasal bahasa Indonesia (`meng-`, `meny-`, `men-`, `mem-`, `peng-`, `peny-`, `pen-`, `pem-`) dan sufiks bahasa Inggris konservatif (`-ing`, `-ies`, `-ied`, `-ed`, `-s`).

## Pembacaan Konteks Layar Lebih Cerdas

- **Jalur cepat editor & terminal.** Saat dokumen/terminal aktif sudah memberikan teks langsung yang kaya, pembacaan pohon UIA langsung dipersingkat agar tetap responsif.
- **Viewport-aware OCR.** Fallback OCR kini memeriksa isi utama secara terpisah dari sidebar/toolbar (`MainTextLength`), memotong sidebar saat diperlukan (`OcrViewport`), dan hanya memakai hasil OCR jika lebih kaya daripada teks UIA.
- **Konteks jendela split-screen pendamping.** Mendeteksi jendela pendamping yang tampak bersebelahan secara cepat lewat Z-order Win32 (tetap mematuhi daftar blokir dan privasi) dan menyertakannya ke konteks layar aktif.
