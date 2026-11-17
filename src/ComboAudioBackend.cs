using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Deskmate
{
    partial class Island
    {
        // Mixes active 44.1 kHz voices into mono float32 at targetRate using linear resampling
        void MixFramesFloat(float[] monoBuf, int frameCount, int targetRate, out bool anyActive)
        {
            Array.Clear(monoBuf, 0, frameCount);
            anyActive = false;
            double step = (double)ComboAudioRate / targetRate;
            const float inv32768 = 1.0f / 32768.0f;

            lock (voiceLock)
            {
                for (int v = 0; v < comboVoices.Length; v++)
                {
                    if (!comboVoices[v].Active) continue;
                    short[] src = comboVoices[v].Samples;
                    int len = src.Length;
                    double p = comboVoices[v].SrcPos;
                    float gain = comboVoices[v].Gain * inv32768;

                    if (p >= len)
                    {
                        comboVoices[v].Active = false;
                        continue;
                    }

                    if (targetRate == ComboAudioRate)
                    {
                        int ip = (int)p;
                        int take = len - ip;
                        if (take > frameCount) take = frameCount;
                        for (int f = 0; f < take; f++)
                            monoBuf[f] += src[ip + f] * gain;
                        p += take;
                    }
                    else
                    {
                        for (int f = 0; f < frameCount; f++)
                        {
                            int idx = (int)p;
                            if (idx >= len) break;
                            double frac = p - idx;
                            float s0 = src[idx];
                            float s1 = (idx + 1 < len) ? src[idx + 1] : 0.0f;
                            monoBuf[f] += (float)(s0 + (s1 - s0) * frac) * gain;
                            p += step;
                        }
                    }

                    comboVoices[v].SrcPos = p;
                    comboVoices[v].Pos = (int)p;
                    if (p >= len) comboVoices[v].Active = false;
                    else anyActive = true;
                }
            }

            for (int f = 0; f < frameCount; f++)
                monoBuf[f] = SoftClip(monoBuf[f]);
        }

        static bool ParseMixFormat(IntPtr fmtPtr, out int channels, out int rate, out int bits, out bool isFloat)
        {
            channels = 0;
            rate = 0;
            bits = 0;
            isFloat = false;
            if (fmtPtr == IntPtr.Zero) return false;

            int tag = Marshal.ReadInt16(fmtPtr, 0) & 0xFFFF;
            channels = Marshal.ReadInt16(fmtPtr, 2) & 0xFFFF;
            rate = Marshal.ReadInt32(fmtPtr, 4);
            bits = Marshal.ReadInt16(fmtPtr, 14) & 0xFFFF;
            if (channels <= 0 || rate <= 0) return false;

            if (tag == 3 && bits == 32)
            {
                isFloat = true;
                return true;
            }
            if (tag == 1 && bits == 16)
            {
                isFloat = false;
                return true;
            }
            if (tag == 0xFFFE)
            {
                int cbSize = Marshal.ReadInt16(fmtPtr, 16) & 0xFFFF;
                if (cbSize >= 22)
                {
                    byte[] gBytes = new byte[16];
                    Marshal.Copy(IntPtr.Add(fmtPtr, 24), gBytes, 0, 16);
                    Guid sub = new Guid(gBytes);
                    if (sub == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT && bits == 32)
                    {
                        isFloat = true;
                        return true;
                    }
                    if (sub == KSDATAFORMAT_SUBTYPE_PCM && bits == 16)
                    {
                        isFloat = false;
                        return true;
                    }
                }
            }
            return false;
        }

        bool OpenWasapiDevice(
            AutoResetEvent audioEvent,
            out IAudioClient client,
            out IAudioRenderClient render,
            out uint bufferFrames,
            out int channels,
            out int rate,
            out bool isFloat,
            out double periodMs)
        {
            client = null;
            render = null;
            bufferFrames = 0;
            channels = 2;
            rate = 48000;
            isFloat = true;
            periodMs = 10.0;

            IMMDeviceEnumerator en = null;
            IMMDevice dev = null;
            IntPtr fmtPtr = IntPtr.Zero;
            IntPtr customFmtPtr = IntPtr.Zero;

            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                int hr = en.GetDefaultAudioEndpoint(eRender, eMultimedia, out dev);
                if (hr != 0 || dev == null) return false;

                Guid iidClient = typeof(IAudioClient).GUID;
                object clientObj;
                hr = dev.Activate(ref iidClient, CLSCTX_ALL, IntPtr.Zero, out clientObj);
                if (hr != 0 || clientObj == null) return false;
                client = (IAudioClient)clientObj;

                hr = client.GetMixFormat(out fmtPtr);
                if (hr != 0 || fmtPtr == IntPtr.Zero)
                {
                    ReleaseWasapiDevice(ref client, ref render);
                    return false;
                }

                int bits;
                int flags = AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_NOPERSIST;
                IntPtr initFmt = fmtPtr;
                if (!ParseMixFormat(fmtPtr, out channels, out rate, out bits, out isFloat))
                {
                    // Endpoint mix format is non-standard (e.g. 24-bit PCM): ask Windows SRC to convert from 48 kHz float32 stereo
                    channels = 2;
                    rate = 48000;
                    isFloat = true;
                    var wfx = new WAVEFORMATEX
                    {
                        wFormatTag = 3, // WAVE_FORMAT_IEEE_FLOAT
                        nChannels = 2,
                        nSamplesPerSec = 48000,
                        wBitsPerSample = 32,
                        nBlockAlign = 8,
                        nAvgBytesPerSec = 48000 * 8,
                        cbSize = 0
                    };
                    customFmtPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WAVEFORMATEX)));
                    Marshal.StructureToPtr(wfx, customFmtPtr, false);
                    initFmt = customFmtPtr;
                    flags |= AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
                }

                hr = client.Initialize(AUDCLNT_SHAREMODE_SHARED, flags, 0, 0, initFmt, IntPtr.Zero);
                if (hr != 0)
                {
                    ReleaseWasapiDevice(ref client, ref render);
                    return false;
                }

                long defPeriod, minPeriod;
                if (client.GetDevicePeriod(out defPeriod, out minPeriod) == 0 && defPeriod > 0)
                    periodMs = defPeriod / 10000.0;

                hr = client.GetBufferSize(out bufferFrames);
                if (hr != 0 || bufferFrames == 0)
                {
                    ReleaseWasapiDevice(ref client, ref render);
                    return false;
                }

                hr = client.SetEventHandle(audioEvent.SafeWaitHandle.DangerousGetHandle());
                if (hr != 0)
                {
                    ReleaseWasapiDevice(ref client, ref render);
                    return false;
                }

                Guid iidRender = typeof(IAudioRenderClient).GUID;
                object renderObj;
                hr = client.GetService(ref iidRender, out renderObj);
                if (hr != 0 || renderObj == null)
                {
                    ReleaseWasapiDevice(ref client, ref render);
                    return false;
                }
                render = (IAudioRenderClient)renderObj;

                // Pre-fill entire initial buffer with clean 0.0f / 0 silence before Start()
                IntPtr dataPtr;
                hr = render.GetBuffer(bufferFrames, out dataPtr);
                if (hr != 0 || dataPtr == IntPtr.Zero)
                {
                    ReleaseWasapiDevice(ref client, ref render);
                    return false;
                }
                int sampleCount = (int)bufferFrames * channels;
                if (isFloat)
                {
                    var zeroF = new float[sampleCount];
                    Marshal.Copy(zeroF, 0, dataPtr, sampleCount);
                }
                else
                {
                    var zeroS = new short[sampleCount];
                    Marshal.Copy(zeroS, 0, dataPtr, sampleCount);
                }
                hr = render.ReleaseBuffer(bufferFrames, 0);
                if (hr != 0)
                {
                    ReleaseWasapiDevice(ref client, ref render);
                    return false;
                }

                hr = client.Start();
                if (hr != 0)
                {
                    ReleaseWasapiDevice(ref client, ref render);
                    return false;
                }

                mixerLastFailTick = 0;
                mixerFailLogged = false;
                return true;
            }
            catch (Exception ex)
            {
                if (!mixerFailLogged)
                {
                    mixerFailLogged = true;
                    Log.Write("combo audio WASAPI open: " + ex.Message);
                }
                ReleaseWasapiDevice(ref client, ref render);
                return false;
            }
            finally
            {
                if (fmtPtr != IntPtr.Zero) CoTaskMemFree(fmtPtr);
                if (customFmtPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(customFmtPtr);
                if (dev != null) try { Marshal.ReleaseComObject(dev); } catch (Exception) { }
                if (en != null) try { Marshal.ReleaseComObject(en); } catch (Exception) { }
            }
        }

        static void ReleaseWasapiDevice(ref IAudioClient client, ref IAudioRenderClient render)
        {
            try { if (client != null) client.Stop(); } catch (Exception) { }
            if (render != null)
            {
                try { Marshal.ReleaseComObject(render); } catch (Exception) { }
                render = null;
            }
            if (client != null)
            {
                try { Marshal.ReleaseComObject(client); } catch (Exception) { }
                client = null;
            }
        }

        bool RunWasapiLoop(ManualResetEvent stopEvent)
        {
            IAudioClient client = null;
            IAudioRenderClient render = null;
            AutoResetEvent audioEvent = new AutoResetEvent(false);
            WaitHandle[] waitHandles = new WaitHandle[] { stopEvent, audioEvent };
            IntPtr mmcssHandle = IntPtr.Zero;

            try
            {
                uint taskIdx;
                try { mmcssHandle = AvSetMmThreadCharacteristicsW("Audio", out taskIdx); }
                catch (Exception) { mmcssHandle = IntPtr.Zero; }

                uint bufferFrames;
                int channels;
                int rate;
                bool isFloat;
                double periodMs;
                if (!OpenWasapiDevice(audioEvent, out client, out render, out bufferFrames, out channels, out rate, out isFloat, out periodMs))
                    return false;

                var monoBuf = new float[bufferFrames];
                float[] outFloats = isFloat ? new float[bufferFrames * channels] : null;
                short[] outShorts = isFloat ? null : new short[bufferFrames * channels];
                var sw = Stopwatch.StartNew();
                long lastWakeMs = sw.ElapsedMilliseconds;
                bool streamPrimed = false;

                while (true)
                {
                    int signaled = WaitHandle.WaitAny(waitHandles, 200);
                    if (signaled == 0) break;
                    lock (mixerLifecycleLock)
                    {
                        if (mixerStopping) break;
                    }

                    long nowMs = sw.ElapsedMilliseconds;
                    long elapsedMs = nowMs - lastWakeMs;
                    lastWakeMs = nowMs;

                    uint padding;
                    int hr = client.GetCurrentPadding(out padding);
                    if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr < 0)
                    {
                        ReleaseWasapiDevice(ref client, ref render);
                        if (hr == AUDCLNT_E_DEVICE_INVALIDATED)
                        {
                            if (stopEvent.WaitOne(150)) break;
                            if (!OpenWasapiDevice(audioEvent, out client, out render, out bufferFrames, out channels, out rate, out isFloat, out periodMs))
                            {
                                mixerLastFailTick = Environment.TickCount;
                                break;
                            }
                            if (monoBuf.Length < bufferFrames) monoBuf = new float[bufferFrames];
                            outFloats = isFloat ? new float[bufferFrames * channels] : null;
                            outShorts = isFloat ? null : new short[bufferFrames * channels];
                            streamPrimed = false;
                            lastWakeMs = sw.ElapsedMilliseconds;
                            continue;
                        }
                        mixerLastFailTick = Environment.TickCount;
                        break;
                    }

                    bool anyVoice = HasActiveVoices();
                    if (!ShouldKeepStreamAlive(anyVoice))
                    {
                        // User disabled Combo/ComboSound and tail finished -> stop cleanly
                        break;
                    }

                    if (streamPrimed && anyVoice && padding == 0 && elapsedMs > periodMs * 1.6)
                        mixerUnderruns++;

                    if (padding >= bufferFrames)
                        continue;

                    int framesAvailable = (int)(bufferFrames - padding);
                    IntPtr dataPtr;
                    hr = render.GetBuffer((uint)framesAvailable, out dataPtr);
                    if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr < 0 || dataPtr == IntPtr.Zero)
                    {
                        ReleaseWasapiDevice(ref client, ref render);
                        if (hr == AUDCLNT_E_DEVICE_INVALIDATED)
                        {
                            if (stopEvent.WaitOne(150)) break;
                            if (!OpenWasapiDevice(audioEvent, out client, out render, out bufferFrames, out channels, out rate, out isFloat, out periodMs))
                            {
                                mixerLastFailTick = Environment.TickCount;
                                break;
                            }
                            if (monoBuf.Length < bufferFrames) monoBuf = new float[bufferFrames];
                            outFloats = isFloat ? new float[bufferFrames * channels] : null;
                            outShorts = isFloat ? null : new short[bufferFrames * channels];
                            streamPrimed = false;
                            lastWakeMs = sw.ElapsedMilliseconds;
                            continue;
                        }
                        mixerLastFailTick = Environment.TickCount;
                        break;
                    }

                    if (anyVoice)
                    {
                        bool stillActive;
                        MixFramesFloat(monoBuf, framesAvailable, rate, out stillActive);
                        if (isFloat)
                        {
                            for (int f = 0; f < framesAvailable; f++)
                            {
                                float s = monoBuf[f];
                                int baseIdx = f * channels;
                                for (int c = 0; c < channels; c++)
                                    outFloats[baseIdx + c] = s;
                            }
                            Marshal.Copy(outFloats, 0, dataPtr, framesAvailable * channels);
                        }
                        else
                        {
                            for (int f = 0; f < framesAvailable; f++)
                            {
                                int iv = (int)Math.Round(monoBuf[f] * 32767.0f);
                                if (iv > 32767) iv = 32767;
                                else if (iv < -32768) iv = -32768;
                                short sv = (short)iv;
                                int baseIdx = f * channels;
                                for (int c = 0; c < channels; c++)
                                    outShorts[baseIdx + c] = sv;
                            }
                            Marshal.Copy(outShorts, 0, dataPtr, framesAvailable * channels);
                        }
                    }
                    else
                    {
                        int totalSamples = framesAvailable * channels;
                        if (isFloat)
                        {
                            Array.Clear(outFloats, 0, totalSamples);
                            Marshal.Copy(outFloats, 0, dataPtr, totalSamples);
                        }
                        else
                        {
                            Array.Clear(outShorts, 0, totalSamples);
                            Marshal.Copy(outShorts, 0, dataPtr, totalSamples);
                        }
                    }

                    hr = render.ReleaseBuffer((uint)framesAvailable, 0);
                    if (hr < 0)
                    {
                        ReleaseWasapiDevice(ref client, ref render);
                        mixerLastFailTick = Environment.TickCount;
                        break;
                    }
                    streamPrimed = true;
                }
                return true;
            }
            finally
            {
                ReleaseWasapiDevice(ref client, ref render);
                if (mmcssHandle != IntPtr.Zero)
                {
                    try { AvRevertMmThreadCharacteristics(mmcssHandle); } catch (Exception) { }
                }
                try { audioEvent.Dispose(); } catch (Exception) { }
            }
        }

        // Conservative 48 kHz stereo 20 ms x 4 waveOut fallback if WASAPI cannot initialize
        void RunWaveOutFallback(ManualResetEvent stopEvent)
        {
            IntPtr hWaveOut = IntPtr.Zero;
            AutoResetEvent waveEv = new AutoResetEvent(false);
            WaitHandle[] waitHandles = new WaitHandle[] { stopEvent, waveEv };
            var pcmBytes = new byte[FallbackBufCount][];
            var pcmHandles = new GCHandle[FallbackBufCount];
            var hdrPtrs = new IntPtr[FallbackBufCount];
            var inFlight = new bool[FallbackBufCount];
            var monoBuf = new float[FallbackFrames];
            var outShorts = new short[FallbackFrames * 2];
            bool timerSet = false;

            try
            {
                try { timerSet = timeBeginPeriod(1) == 0; } catch (Exception) { timerSet = false; }

                var fmt = new WAVEFORMATEX
                {
                    wFormatTag = 1,
                    nChannels = 2,
                    nSamplesPerSec = FallbackRate,
                    wBitsPerSample = 16,
                    nBlockAlign = 4,
                    nAvgBytesPerSec = FallbackRate * 4,
                    cbSize = 0
                };
                int mmr = waveOutOpen(out hWaveOut, WAVE_MAPPER, ref fmt, waveEv.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
                if (mmr != 0 || hWaveOut == IntPtr.Zero)
                {
                    mixerLastFailTick = Environment.TickCount;
                    if (!mixerFailLogged)
                    {
                        mixerFailLogged = true;
                        Log.Write("combo audio fallback waveOutOpen: " + mmr);
                    }
                    return;
                }

                for (int i = 0; i < FallbackBufCount; i++)
                {
                    pcmBytes[i] = new byte[FallbackBufBytes];
                    pcmHandles[i] = GCHandle.Alloc(pcmBytes[i], GCHandleType.Pinned);
                    hdrPtrs[i] = Marshal.AllocHGlobal(WaveHdrSize);
                    var hdr = new WAVEHDR
                    {
                        lpData = pcmHandles[i].AddrOfPinnedObject(),
                        dwBufferLength = (uint)FallbackBufBytes,
                        dwBytesRecorded = 0,
                        dwUser = IntPtr.Zero,
                        dwFlags = 0,
                        dwLoops = 0,
                        lpNext = IntPtr.Zero,
                        reserved = IntPtr.Zero
                    };
                    Marshal.StructureToPtr(hdr, hdrPtrs[i], false);
                    if (waveOutPrepareHeader(hWaveOut, hdrPtrs[i], WaveHdrSize) != 0)
                        return;
                    // Pre-queue all 4 buffers with silence before entering the wait loop
                    if (waveOutWrite(hWaveOut, hdrPtrs[i], WaveHdrSize) != 0)
                        return;
                    inFlight[i] = true;
                }

                while (true)
                {
                    int signaled = WaitHandle.WaitAny(waitHandles, 100);
                    if (signaled == 0) break;
                    lock (mixerLifecycleLock)
                    {
                        if (mixerStopping) break;
                    }

                    bool anyVoice = HasActiveVoices();
                    if (!ShouldKeepStreamAlive(anyVoice))
                        break;

                    int doneCount = 0;
                    for (int i = 0; i < FallbackBufCount; i++)
                    {
                        if (inFlight[i])
                        {
                            uint flags = (uint)Marshal.ReadInt32(hdrPtrs[i], WaveHdrFlagsOffset);
                            if ((flags & WHDR_DONE) != 0)
                            {
                                inFlight[i] = false;
                                doneCount++;
                            }
                        }
                    }

                    if (doneCount == FallbackBufCount && anyVoice)
                        mixerUnderruns++;

                    for (int i = 0; i < FallbackBufCount; i++)
                    {
                        if (inFlight[i]) continue;
                        if (HasActiveVoices())
                        {
                            bool stillActive;
                            MixFramesFloat(monoBuf, FallbackFrames, FallbackRate, out stillActive);
                            for (int f = 0; f < FallbackFrames; f++)
                            {
                                int iv = (int)Math.Round(monoBuf[f] * 32767.0f);
                                if (iv > 32767) iv = 32767;
                                else if (iv < -32768) iv = -32768;
                                short sv = (short)iv;
                                outShorts[f * 2] = sv;
                                outShorts[f * 2 + 1] = sv;
                            }
                            Buffer.BlockCopy(outShorts, 0, pcmBytes[i], 0, FallbackBufBytes);
                        }
                        else
                        {
                            Array.Clear(pcmBytes[i], 0, FallbackBufBytes);
                        }
                        if (waveOutWrite(hWaveOut, hdrPtrs[i], WaveHdrSize) != 0)
                        {
                            mixerLastFailTick = Environment.TickCount;
                            return;
                        }
                        inFlight[i] = true;
                    }
                }
            }
            finally
            {
                if (hWaveOut != IntPtr.Zero)
                {
                    try { waveOutReset(hWaveOut); } catch (Exception) { }
                    for (int i = 0; i < FallbackBufCount; i++)
                    {
                        if (hdrPtrs[i] != IntPtr.Zero)
                        {
                            uint flags = (uint)Marshal.ReadInt32(hdrPtrs[i], WaveHdrFlagsOffset);
                            if ((flags & WHDR_PREPARED) != 0)
                            {
                                try { waveOutUnprepareHeader(hWaveOut, hdrPtrs[i], WaveHdrSize); } catch (Exception) { }
                            }
                        }
                    }
                    try { waveOutClose(hWaveOut); } catch (Exception) { }
                }
                for (int i = 0; i < FallbackBufCount; i++)
                {
                    if (hdrPtrs[i] != IntPtr.Zero) Marshal.FreeHGlobal(hdrPtrs[i]);
                    if (pcmHandles[i].IsAllocated) pcmHandles[i].Free();
                }
                if (timerSet)
                {
                    try { timeEndPeriod(1); } catch (Exception) { }
                }
                try { waveEv.Dispose(); } catch (Exception) { }
            }
        }

        void MixerLoop(ManualResetEvent stopEvent)
        {
            try
            {
                if (!RunWasapiLoop(stopEvent))
                {
                    bool stopping;
                    lock (mixerLifecycleLock) { stopping = mixerStopping; }
                    if (!stopping)
                        RunWaveOutFallback(stopEvent);
                }
            }
            catch (Exception ex)
            {
                mixerLastFailTick = Environment.TickCount;
                Log.Write("combo audio mixer: " + ex.Message);
            }
            finally
            {
                lock (mixerLifecycleLock)
                {
                    if (ReferenceEquals(mixerStopEvent, stopEvent))
                        mixerStopEvent = null;
                    if (ReferenceEquals(mixerThread, Thread.CurrentThread))
                        mixerThread = null;
                }
                try { stopEvent.Dispose(); } catch (Exception) { }
            }
        }
    }
}
