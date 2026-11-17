using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Deskmate
{
    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint numFramesRequested, out IntPtr data);
        [PreserveSig] int ReleaseBuffer(uint numFramesWritten, uint flags);
    }

    // Polyphonic WASAPI shared-mode event-driven mixer + 44.1 kHz 16-bit PCM synthesizer
    partial class Island
    {
        const int ComboAudioRate = 44100;
        const int MixerMaxVoices = 24;
        const int MixerIdleShutdownMs = 2500;

        // WASAPI constants
        const int AUDCLNT_SHAREMODE_SHARED = 0;
        const int AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
        const int AUDCLNT_STREAMFLAGS_NOPERSIST = 0x00080000;
        const int AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
        const int AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = unchecked((int)0x80000000);
        const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
        const int CLSCTX_ALL = 23;
        const int eRender = 0;
        const int eMultimedia = 1;
        static readonly Guid KSDATAFORMAT_SUBTYPE_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");
        static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new Guid("00000003-0000-0010-8000-00AA00389B71");

        // WinMM waveOut fallback constants
        const uint WAVE_MAPPER = unchecked((uint)-1);
        const uint CALLBACK_EVENT = 0x00050000;
        const uint WHDR_DONE = 0x00000001;
        const uint WHDR_PREPARED = 0x00000002;
        const int FallbackRate = 48000;
        const int FallbackFrames = 960; // 20 ms at 48 kHz
        const int FallbackBufBytes = FallbackFrames * 4; // 16-bit stereo
        const int FallbackBufCount = 4;

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("ole32.dll")]
        static extern void CoTaskMemFree(IntPtr pv);

        [DllImport("avrt.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, out uint taskIndex);

        [DllImport("avrt.dll")]
        static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);

        [DllImport("winmm.dll")]
        static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        static extern uint timeEndPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        static extern int waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

        [DllImport("winmm.dll")]
        static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll")]
        static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll")]
        static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll")]
        static extern int waveOutReset(IntPtr hwo);

        [DllImport("winmm.dll")]
        static extern int waveOutClose(IntPtr hwo);

        sealed class PinnedWav
        {
            public byte[] Data;
            public short[] Samples;
            public GCHandle Handle;
            public IntPtr Ptr;
        }

        struct AudioVoice
        {
            public short[] Samples;
            public int Pos;
            public double SrcPos;
            public float Gain;
            public bool Active;
        }

        static readonly int WaveHdrSize = Marshal.SizeOf(typeof(WAVEHDR));
        static readonly int WaveHdrFlagsOffset = Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags").ToInt32();

        bool comboAudioReady;
        readonly List<PinnedWav> comboAudioAll = new List<PinnedWav>();
        PinnedWav[] comboArcadeBlips;
        PinnedWav[] comboThockVariants;
        PinnedWav[] comboClickVariants;
        PinnedWav[] comboPianoNotes;
        PinnedWav[] comboTierChimes;
        PinnedWav comboPbFanfareWav;
        PinnedWav comboCriticalWav;
        PinnedWav comboBrokenWav;
        PinnedWav comboShieldCrackWav;

        readonly object voiceLock = new object();
        readonly object mixerLifecycleLock = new object();
        readonly AudioVoice[] comboVoices = new AudioVoice[MixerMaxVoices];
        Thread mixerThread;
        ManualResetEvent mixerStopEvent;
        bool mixerStopping;
        bool mixerHasPlayed;
        int mixerLastVoiceTick;
        int mixerLastFailTick;
        bool mixerFailLogged;
        volatile int mixerUnderruns;
        public int ComboAudioUnderruns { get { return mixerUnderruns; } }
        public bool ComboAudioHasPlayed { get { return mixerHasPlayed; } }

        void EnsureComboAudio()
        {
            if (comboAudioReady) return;
            comboAudioReady = true;

            // 50 ascending arcade blips (440 Hz -> 1240 Hz, ~36ms)
            comboArcadeBlips = new PinnedWav[50];
            for (int i = 0; i < 50; i++)
            {
                double t = i / 49.0;
                double freq = 440.0 + t * 800.0;
                comboArcadeBlips[i] = PinWav(SynthArcadeBlip(freq, 0.036));
            }

            // 6 organic mechanical thock variants (~48ms, damped 250-320 Hz body + woody PBT knock)
            comboThockVariants = new PinnedWav[6];
            for (int i = 0; i < 6; i++)
            {
                double baseFreq = 250.0 + i * 14.0;
                comboThockVariants[i] = PinWav(SynthThock(baseFreq, 0.048, i));
            }

            // 6 crisp tactile click variants (~30ms, clickbar impulse + subtle bottom-out body)
            comboClickVariants = new PinnedWav[6];
            for (int i = 0; i < 6; i++)
            {
                double resFreq = 1900.0 + i * 115.0;
                comboClickVariants[i] = PinWav(SynthClick(resFreq, 0.030, i));
            }

            // 11 consonant C-major pentatonic piano notes (G4-G6, ~240ms)
            double[] pianoFreqs = {
                392.00, 440.00, 523.25, 587.33, 659.25,
                783.99, 880.00, 1046.50, 1174.66, 1318.51,
                1567.98
            };
            comboPianoNotes = new PinnedWav[pianoFreqs.Length];
            for (int i = 0; i < pianoFreqs.Length; i++)
                comboPianoNotes[i] = PinWav(SynthPianoNote(pianoFreqs[i], 0.24, i));

            // Tier-up chimes (tiers 2..6 -> indices 0..4)
            comboTierChimes = new PinnedWav[5];
            double[] tierRoots = { 523.25, 587.33, 659.25, 783.99, 880.00 };
            for (int i = 0; i < 5; i++)
                comboTierChimes[i] = PinWav(SynthTierChime(tierRoots[i], 0.20 + i * 0.02));

            comboPbFanfareWav = PinWav(SynthPbFanfare());
            comboCriticalWav = PinWav(SynthCritical());
            comboBrokenWav = PinWav(SynthBroken());
            comboShieldCrackWav = PinWav(SynthShieldCrack());
        }

        PinnedWav PinWav(short[] samples)
        {
            byte[] wav = BuildWav(samples);
            var h = GCHandle.Alloc(wav, GCHandleType.Pinned);
            var pw = new PinnedWav { Data = wav, Samples = samples, Handle = h, Ptr = h.AddrOfPinnedObject() };
            comboAudioAll.Add(pw);
            return pw;
        }

        void ClearComboAudioVoices()
        {
            lock (voiceLock)
            {
                for (int i = 0; i < comboVoices.Length; i++)
                    comboVoices[i].Active = false;
                mixerHasPlayed = false;
            }
        }

        public void StopComboAudio()
        {
            Thread t;
            ManualResetEvent stopEv;
            lock (mixerLifecycleLock)
            {
                mixerStopping = true;
                stopEv = mixerStopEvent;
                t = mixerThread;
                mixerThread = null;
            }
            if (stopEv != null)
            {
                try { stopEv.Set(); } catch (Exception) { }
            }
            if (t != null && t.IsAlive)
            {
                try { t.Join(1000); } catch (Exception) { }
            }
            ClearComboAudioVoices();
            mixerUnderruns = 0;
            for (int i = 0; i < comboAudioAll.Count; i++)
            {
                if (comboAudioAll[i].Handle.IsAllocated)
                    comboAudioAll[i].Handle.Free();
            }
            comboAudioAll.Clear();
            comboAudioReady = false;
        }

        bool ComboAudioAllowed(bool test)
        {
            if (test) return true;
            return ComboSoundOn && !comboPaused && !Quiet && !fullscreen && !PrompterOn;
        }

        void EnsureMixerStarted()
        {
            lock (mixerLifecycleLock)
            {
                if (IsDisposed) return;
                if (mixerThread != null && mixerThread.IsAlive) return;
                if (mixerLastFailTick != 0 && (uint)(Environment.TickCount - mixerLastFailTick) < 5000u) return;
                mixerStopping = false;
                mixerUnderruns = 0;
                var stopEv = new ManualResetEvent(false);
                mixerStopEvent = stopEv;
                var t = new Thread(delegate () { MixerLoop(stopEv); })
                {
                    IsBackground = true,
                    Name = "DeskmateComboAudio",
                    Priority = ThreadPriority.Highest
                };
                t.SetApartmentState(ApartmentState.MTA);
                mixerThread = t;
                t.Start();
            }
        }

        void PlayPinned(PinnedWav pw, float gain = 1.0f)
        {
            if (pw == null || pw.Samples == null || pw.Samples.Length == 0) return;
            EnsureMixerStarted();
            lock (mixerLifecycleLock)
            {
                if (mixerStopping) return;
            }
            lock (voiceLock)
            {
                int slot = -1;
                int bestPos = -1;
                int oldestSlot = 0;
                for (int i = 0; i < comboVoices.Length; i++)
                {
                    if (!comboVoices[i].Active) { slot = i; break; }
                    if (comboVoices[i].Pos > bestPos)
                    {
                        bestPos = comboVoices[i].Pos;
                        oldestSlot = i;
                    }
                }
                if (slot < 0) slot = oldestSlot;
                comboVoices[slot].Samples = pw.Samples;
                comboVoices[slot].Pos = 0;
                comboVoices[slot].SrcPos = 0.0;
                comboVoices[slot].Gain = gain;
                comboVoices[slot].Active = true;
                mixerLastVoiceTick = Environment.TickCount;
                mixerHasPlayed = true;
            }
        }

        static float SoftClip(float v)
        {
            if (v > 0.85f)
            {
                float over = v - 0.85f;
                return 0.85f + 0.14f * (over / (over + 0.14f));
            }
            if (v < -0.85f)
            {
                float over = -0.85f - v;
                return -0.85f - 0.14f * (over / (over + 0.14f));
            }
            return v;
        }

        bool HasActiveVoices()
        {
            lock (voiceLock)
            {
                for (int v = 0; v < comboVoices.Length; v++)
                {
                    if (comboVoices[v].Active) return true;
                }
                return false;
            }
        }

        bool ShouldKeepStreamAlive(bool anyVoiceActive)
        {
            if (anyVoiceActive) return true;
            if (ComboOn && ComboSoundOn && !comboPaused && !Quiet && !fullscreen && !PrompterOn) return true;
            int lastTick;
            lock (voiceLock) { lastTick = mixerLastVoiceTick; }
            return (uint)(Environment.TickCount - lastTick) < (uint)MixerIdleShutdownMs;
        }

    }
}
