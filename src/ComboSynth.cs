using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Deskmate
{
    partial class Island
    {
        void PlayComboKeySound(int count, bool test)
        {
            if (!ComboAudioAllowed(test)) return;
            string prof = ComboSoundProfile;
            if (prof == "classic") return;
            EnsureComboAudio();
            if (prof == "thocky")
            {
                int idx = Math.Abs(count) % comboThockVariants.Length;
                PlayPinned(comboThockVariants[idx]);
            }
            else if (prof == "clicky")
            {
                int idx = Math.Abs(count) % comboClickVariants.Length;
                PlayPinned(comboClickVariants[idx]);
            }
            else if (prof == "piano")
            {
                int span = comboPianoNotes.Length - 1;
                int wave = Math.Abs(count - 1) % (span * 2);
                int idx = wave <= span ? wave : (span * 2 - wave);
                PlayPinned(comboPianoNotes[idx]);
            }
            else
            {
                int idx = Math.Max(0, Math.Min(comboArcadeBlips.Length - 1, count - 1));
                PlayPinned(comboArcadeBlips[idx]);
            }
        }

        void PlayComboTierUpSound(int tier)
        {
            if (!ComboAudioAllowed(false) || tier < 2) return;
            EnsureComboAudio();
            int idx = Math.Max(0, Math.Min(comboTierChimes.Length - 1, tier - 2));
            PlayPinned(comboTierChimes[idx], 0.92f);
        }

        void PlayComboPbFanfare()
        {
            if (!ComboAudioAllowed(false)) return;
            EnsureComboAudio();
            PlayPinned(comboPbFanfareWav, 0.95f);
        }

        void PlayComboCriticalSound()
        {
            if (!ComboAudioAllowed(false)) return;
            EnsureComboAudio();
            PlayPinned(comboCriticalWav, 0.95f);
        }

        void PlayComboBrokenSound()
        {
            if (!ComboAudioAllowed(false)) return;
            EnsureComboAudio();
            PlayPinned(comboBrokenWav, 0.90f);
        }

        void PlayComboShieldCrackSound()
        {
            if (!ComboAudioAllowed(false)) return;
            EnsureComboAudio();
            PlayPinned(comboShieldCrackWav, 0.92f);
        }

        public void PlayComboTestSound()
        {
            EnsureComboAudio();
            string prof = ComboSoundProfile;
            if (prof == "classic")
                PlayPinned(comboTierChimes[1]);
            else if (prof == "piano")
                PlayComboKeySound(6, true);
            else
                PlayComboKeySound(18, true);
        }

        static byte[] BuildWav(short[] samples)
        {
            int dataBytes = samples.Length * 2;
            byte[] buf = new byte[44 + dataBytes];
            // RIFF header
            buf[0] = (byte)'R'; buf[1] = (byte)'I'; buf[2] = (byte)'F'; buf[3] = (byte)'F';
            WriteInt32(buf, 4, 36 + dataBytes);
            buf[8] = (byte)'W'; buf[9] = (byte)'A'; buf[10] = (byte)'V'; buf[11] = (byte)'E';
            // fmt chunk
            buf[12] = (byte)'f'; buf[13] = (byte)'m'; buf[14] = (byte)'t'; buf[15] = (byte)' ';
            WriteInt32(buf, 16, 16); // PCM
            WriteInt16(buf, 20, 1);  // AudioFormat = 1
            WriteInt16(buf, 22, 1);  // Mono
            WriteInt32(buf, 24, ComboAudioRate);
            WriteInt32(buf, 28, ComboAudioRate * 2);
            WriteInt16(buf, 32, 2);  // BlockAlign
            WriteInt16(buf, 34, 16); // BitsPerSample
            // data chunk
            buf[36] = (byte)'d'; buf[37] = (byte)'a'; buf[38] = (byte)'t'; buf[39] = (byte)'a';
            WriteInt32(buf, 40, dataBytes);
            Buffer.BlockCopy(samples, 0, buf, 44, dataBytes);
            return buf;
        }

        static void WriteInt32(byte[] b, int off, int v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
            b[off + 2] = (byte)((v >> 16) & 0xFF);
            b[off + 3] = (byte)((v >> 24) & 0xFF);
        }

        static void WriteInt16(byte[] b, int off, short v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
        }

        static short ClampSample(double v)
        {
            int iv = (int)Math.Round(v * 32767.0);
            if (iv > 32767) return 32767;
            if (iv < -32768) return -32768;
            return (short)iv;
        }

        static void ApplyFadeOut(short[] s, int fadeSamples)
        {
            if (s == null || s.Length == 0 || fadeSamples <= 0) return;
            int count = Math.Min(fadeSamples, s.Length);
            int start = s.Length - count;
            for (int i = 0; i < count; i++)
            {
                double g = (double)(count - 1 - i) / count;
                s[start + i] = (short)Math.Round(s[start + i] * g);
            }
        }

        static void RemoveDc(short[] s)
        {
            if (s == null || s.Length == 0) return;
            double prevX = s[0];
            double prevY = 0;
            s[0] = 0;
            const double r = 0.995;
            for (int i = 1; i < s.Length; i++)
            {
                double x = s[i];
                double y = x - prevX + r * prevY;
                prevX = x;
                prevY = y;
                int iv = (int)Math.Round(y);
                if (iv > 32767) iv = 32767;
                else if (iv < -32768) iv = -32768;
                s[i] = (short)iv;
            }
        }

        static short[] SynthArcadeBlip(double freq, double durSec)
        {
            int n = (int)(ComboAudioRate * durSec);
            var s = new short[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ComboAudioRate;
                double env = Math.Min(1.0, t / 0.0014) * Math.Exp(-t * 85.0);
                double f = freq * (1.0 + t * 5.0);
                phase += 2.0 * Math.PI * f / ComboAudioRate;
                double sine = Math.Sin(phase);
                double tri = 2.0 * Math.Abs(2.0 * ((phase / (2.0 * Math.PI)) - Math.Floor(phase / (2.0 * Math.PI) + 0.5))) - 1.0;
                s[i] = ClampSample((sine * 0.80 + tri * 0.20) * env * 0.42);
            }
            RemoveDc(s);
            ApplyFadeOut(s, (int)(ComboAudioRate * 0.008));
            return s;
        }

        static short[] SynthThock(double freq, double durSec, int seed)
        {
            int n = (int)(ComboAudioRate * durSec);
            var s = new short[n];
            double phase = 0;
            uint rng = (uint)(0x9E3779B9u + seed * 1013904223u);
            double lp = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ComboAudioRate;
                double bodyEnv = Math.Min(1.0, t / 0.0014) * Math.Exp(-t * 68.0);
                double f = freq * (1.0 - Math.Min(0.42, t * 16.0));
                phase += 2.0 * Math.PI * f / ComboAudioRate;
                rng = rng * 1664525u + 1013904223u;
                double raw = (rng >> 16) / 32768.0 - 1.0;
                lp += 0.18 * (raw - lp);
                double noise = lp * Math.Exp(-t * 360.0) * 0.10;
                double body = Math.Sin(phase) * 0.78 + Math.Sin(phase * 2.0) * 0.14;
                double knockFreq = freq * 2.4;
                double knock = Math.Sin(2.0 * Math.PI * knockFreq * t) * Math.Exp(-t * 155.0) * 0.34 * Math.Min(1.0, t / 0.0012);
                s[i] = ClampSample((body * bodyEnv + knock + noise * Math.Min(1.0, t / 0.0012)) * 0.82);
            }
            RemoveDc(s);
            ApplyFadeOut(s, (int)(ComboAudioRate * 0.010));
            return s;
        }

        static short[] SynthClick(double resFreq, double durSec, int seed)
        {
            int n = (int)(ComboAudioRate * durSec);
            var s = new short[n];
            double bodyPhase = 0;
            uint rng = (uint)(0x85EBCA6Bu + seed * 2246822519u);
            double lp = 0, hp = 0, prevLp = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ComboAudioRate;
                double clickEnv = Math.Min(1.0, t / 0.0012) * Math.Exp(-t * 165.0);
                double bodyEnv = Math.Min(1.0, t / 0.0012) * Math.Exp(-t * 140.0);
                bodyPhase += 2.0 * Math.PI * (245.0 * (1.0 - Math.Min(0.35, t * 14.0))) / ComboAudioRate;
                rng = rng * 1664525u + 1013904223u;
                double raw = (rng >> 16) / 32768.0 - 1.0;
                lp += 0.35 * (raw - lp);
                hp = 0.85 * (hp + lp - prevLp);
                prevLp = lp;
                double noise = hp * Math.Exp(-t * 380.0) * Math.Min(1.0, t / 0.0012);
                double res = Math.Sin(2.0 * Math.PI * resFreq * t) * 0.65 + Math.Sin(2.0 * Math.PI * (resFreq * 2.1) * t) * 0.35;
                double bottom = Math.Sin(bodyPhase) * 0.35 * bodyEnv;
                s[i] = ClampSample((res * clickEnv * 0.65 + noise * 0.24 + bottom) * 0.48);
            }
            RemoveDc(s);
            ApplyFadeOut(s, (int)(ComboAudioRate * 0.008));
            return s;
        }

        static short[] SynthPianoNote(double freq, double durSec, int seed)
        {
            int n = (int)(ComboAudioRate * durSec);
            var s = new short[n];
            double twoPi = 2.0 * Math.PI;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ComboAudioRate;
                double attack = Math.Min(1.0, t / 0.0018);

                // Fundamental + slight unison string detune
                double h1 = (Math.Sin(twoPi * freq * t) * 0.55 + Math.Sin(twoPi * (freq * 1.0012) * t) * 0.45) * Math.Exp(-t * 10.5);
                double h2 = Math.Sin(twoPi * (freq * 2.0) * t) * Math.Exp(-t * 15.0) * 0.34;
                double h3 = Math.Sin(twoPi * (freq * 3.0) * t) * Math.Exp(-t * 24.0) * 0.16;
                double h4 = Math.Sin(twoPi * (freq * 4.0) * t) * Math.Exp(-t * 36.0) * 0.07;
                double strings = (h1 + h2 + h3 + h4) * 0.62;

                s[i] = ClampSample(strings * attack * 0.36);
            }
            RemoveDc(s);
            ApplyFadeOut(s, (int)(ComboAudioRate * 0.020));
            return s;
        }

        static short[] SynthTierChime(double rootFreq, double durSec)
        {
            int n = (int)(ComboAudioRate * durSec);
            var s = new short[n];
            double f1 = rootFreq, f2 = rootFreq * 1.25, f3 = rootFreq * 1.5, f4 = rootFreq * 2.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ComboAudioRate;
                double env = Math.Min(1.0, t / 0.002) * Math.Exp(-t * 13.5);
                double v = Math.Sin(2.0 * Math.PI * f1 * t) * 0.40
                         + Math.Sin(2.0 * Math.PI * f2 * t) * 0.26
                         + Math.Sin(2.0 * Math.PI * f3 * t) * 0.22
                         + Math.Sin(2.0 * Math.PI * f4 * t) * 0.12;
                s[i] = ClampSample(v * env * 0.38);
            }
            RemoveDc(s);
            ApplyFadeOut(s, (int)(ComboAudioRate * 0.015));
            return s;
        }

        static short[] SynthPbFanfare()
        {
            double durSec = 0.28;
            int n = (int)(ComboAudioRate * durSec);
            var s = new short[n];
            double[] notes = { 523.25, 659.25, 783.99, 1046.50 };
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ComboAudioRate;
                double sum = 0;
                for (int k = 0; k < notes.Length; k++)
                {
                    double nt = t - k * 0.055;
                    if (nt < 0) continue;
                    double env = Math.Min(1.0, nt / 0.002) * Math.Exp(-nt * 16.0);
                    double f = notes[k];
                    sum += (Math.Sin(2.0 * Math.PI * f * nt) * 0.72 + Math.Sin(2.0 * Math.PI * f * 2.0 * nt) * 0.28) * env;
                }
                s[i] = ClampSample(sum * 0.32);
            }
            RemoveDc(s);
            ApplyFadeOut(s, (int)(ComboAudioRate * 0.015));
            return s;
        }

        static short[] SynthCritical()
        {
            double durSec = 0.16;
            int n = (int)(ComboAudioRate * durSec);
            var s = new short[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ComboAudioRate;
                double f = t < 0.055 ? 620.0 : 980.0;
                double nt = t < 0.055 ? t : (t - 0.055);
                double env = Math.Min(1.0, nt / 0.0015) * Math.Exp(-nt * 24.0);
                double v = Math.Sin(2.0 * Math.PI * f * nt) * 0.7 + Math.Sin(2.0 * Math.PI * f * 1.5 * nt) * 0.3;
                s[i] = ClampSample(v * env * 0.58);
            }
            RemoveDc(s);
            ApplyFadeOut(s, (int)(ComboAudioRate * 0.012));
            return s;
        }

        static short[] SynthBroken()
        {
            double durSec = 0.18;
            int n = (int)(ComboAudioRate * durSec);
            var s = new short[n];
            double phase = 0;
            uint rng = 0x12345678u;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ComboAudioRate;
                double f = 320.0 * Math.Exp(-t * 7.5);
                phase += 2.0 * Math.PI * f / ComboAudioRate;
                double env = Math.Min(1.0, t / 0.0015) * Math.Exp(-t * 16.0);
                rng = rng * 1664525u + 1013904223u;
                double crunch = ((rng >> 16) / 32768.0 - 1.0) * 0.25;
                s[i] = ClampSample((Math.Sin(phase) * 0.75 + crunch) * env * 0.54);
            }
            RemoveDc(s);
            ApplyFadeOut(s, (int)(ComboAudioRate * 0.012));
            return s;
        }

        static short[] SynthShieldCrack()
        {
            double durSec = 0.085;
            int n = (int)(ComboAudioRate * durSec);
            var s = new short[n];
            uint rng = 0xCAFEBABEu;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ComboAudioRate;
                double env = Math.Min(1.0, t / 0.0012) * Math.Exp(-t * 52.0);
                rng = rng * 1664525u + 1013904223u;
                double noise = ((rng >> 16) / 32768.0 - 1.0) * 0.4;
                double tone = Math.Sin(2.0 * Math.PI * 1240.0 * t) * 0.6;
                s[i] = ClampSample((tone + noise) * env * 0.52);
            }
            RemoveDc(s);
            ApplyFadeOut(s, (int)(ComboAudioRate * 0.010));
            return s;
        }
    }
}
