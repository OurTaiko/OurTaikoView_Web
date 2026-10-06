using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
using ManagedBass;
using ManagedBass.Aac;
using ManagedBass.Opus;
using ManagedBass.Fx;
using ManagedBass.Mix;
#endif

namespace OurTaiko
{
    // Port of TeamMajdata's BassHelper, BassSimpleAudioSample and BassAudioSample.
    // Owns the encoded file, never an AudioClip-derived PCM copy.
    public sealed class NativeAudioSample : IDisposable
    {
        static readonly HashSet<NativeAudioSample> samples = new();
        public bool IsDisposed { get; private set; }
        internal static void ReleaseAll()
        {
            lock (AudioEngine.DeviceLock)
                foreach (var sample in new List<NativeAudioSample>(samples)) sample.Dispose();
        }
        static int liveStreams;
        public static int LiveStreams => System.Threading.Volatile.Read(ref liveStreams);
        public int EncodedBytes { get; private set; }
#if !UNITY_WEBGL || UNITY_EDITOR
        public double Length { get; private set; }
#endif
        public float Gain { get; private set; } = 1;
        public string Format { get; private set; }
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
        GCHandle data;
        int stream, decode, resampler;
        readonly bool mixed;
        bool disposed => IsDisposed;
        readonly bool speedChange;
        public bool Playing => !disposed && Bass.ChannelIsActive(stream) == PlaybackState.Playing
            && (!mixed || !BassMix.ChannelHasFlag(stream, BassFlags.MixerChanPause));
        public double Position => disposed ? 0 : Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetPosition(stream));
        static int Open(IntPtr address, long size, BassFlags flags)
        {
            int handle = Bass.CreateStream(address, 0, size, flags);
            if (handle == 0 && Bass.LastError == Errors.FileFormat)
                handle = BassOpus.CreateStream(address, 0, size, flags);
#if UNITY_ANDROID || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_WIN || UNITY_EDITOR_LINUX
            if (handle == 0 && Bass.LastError == Errors.FileFormat)
                handle = BassAac.CreateStream(address, 0, size, flags);
#endif
            if (handle == 0) throw new InvalidOperationException("BASS decode: " + Bass.LastError);
            System.Threading.Interlocked.Increment(ref liveStreams);
            return handle;
        }
        static void Check(bool success, string action)
        {
            if (!success) throw new InvalidOperationException(action + ": " + Bass.LastError);
        }
        public NativeAudioSample(byte[] encoded, AudioEngine engine, bool normalize = true, bool speedChange = false, int? generation = null)
        {
            lock (AudioEngine.DeviceLock)
            {
                if ((generation.HasValue && generation != engine.Generation) || !engine.Native)
                    throw new OperationCanceledException("Audio output changed during preparation");
                if (encoded == null || encoded.Length == 0) throw new ArgumentException("Empty audio file");
                mixed = engine.Backend == AudioBackend.Wasapi || engine.Backend == AudioBackend.Asio;
                this.speedChange = speedChange;
                EncodedBytes = encoded.Length;
                data = GCHandle.Alloc(encoded, GCHandleType.Pinned);
                try
                {
                    decode = Open(data.AddrOfPinnedObject(), encoded.LongLength, BassFlags.Decode | BassFlags.Prescan | BassFlags.AsyncFile);
                    Format = Bass.ChannelGetInfo(decode).ChannelType.ToString();
                    if (normalize)
                    {
                        long length = Bass.ChannelGetLength(decode), previous = -1;
                        double peak = 0;
                        long position;
                        while ((position = Bass.ChannelGetPosition(decode)) >= 0 && position < length && position != previous)
                        {
                            previous = position;
                            int level = Bass.ChannelGetLevel(decode);
                            if (level < 0) break;
                            peak = Math.Max(peak, (level & 0xffff) / 32768.0);
                        }
                        Gain = peak > 0 ? (float)(1 / peak) : 1;
                        Check(Bass.ChannelSetPosition(decode, 0), "Reset normalized stream");
                    }
                    if (speedChange)
                    {
                        stream = BassFx.TempoCreate(decode, mixed ? BassFlags.Decode : BassFlags.Default);
                        if (stream == 0) throw new InvalidOperationException("BASS FX: " + Bass.LastError);
                        System.Threading.Interlocked.Increment(ref liveStreams);
                    }
                    else stream = mixed ? decode : Open(data.AddrOfPinnedObject(), encoded.LongLength, BassFlags.Prescan | BassFlags.AsyncFile);
                    // Decode-only channels have no playback buffer; BASS may return BASS_ERROR_ILLTYPE.
                    if (mixed) Bass.ChannelSetAttribute(stream, ChannelAttribute.Buffer, 0);
                    else Check(Bass.ChannelSetAttribute(stream, ChannelAttribute.Buffer, 0), "Disable stream buffering");
                    Length = Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetLength(stream));
                    if (mixed)
                    {
                        int rate = (int)Bass.ChannelGetAttribute(engine.Mixer, ChannelAttribute.Frequency);
                        resampler = BassMix.CreateMixerStream(rate, 2, BassFlags.Decode | BassFlags.Float);
                        if (resampler == 0) throw new InvalidOperationException("BASS resampler: " + Bass.LastError);
                        System.Threading.Interlocked.Increment(ref liveStreams);
                        Bass.ChannelSetAttribute(resampler, ChannelAttribute.Buffer, 0);
                        Check(BassMix.MixerAddChannel(resampler, stream, BassFlags.MixerChanPause), "Attach sample");
                        Check(BassMix.MixerAddChannel(engine.Mixer, resampler, BassFlags.MixerChanMatrix), "Attach resampler");
                        Check(BassMix.ChannelSetMatrix(resampler, engine.MixingMatrix), "Set output matrix");
                    }
                    else Bass.ChannelStop(stream);
                }
                catch { Dispose(); throw; }
                samples.Add(this);
            }
        }
        public float OutputVolume => disposed ? 0 : (float)Bass.ChannelGetAttribute(stream, ChannelAttribute.Volume);
        public void SetVolume(float volume)
        {
            if (!disposed) Check(Bass.ChannelSetAttribute(stream, ChannelAttribute.Volume, Math.Max(0, volume) * Gain), "Set volume");
        }
        public void Play(float volume, bool loop, double position = 0, float speed = 1, double? delay = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(NativeAudioSample));
            SetVolume(volume);
            Bass.ChannelFlags(stream, loop ? BassFlags.Loop : BassFlags.Default, BassFlags.Loop);
            if (speedChange) Bass.ChannelSetAttribute(stream, ChannelAttribute.Tempo, (speed - 1) * 100);
            if (mixed)
            {
                Check(BassMix.ChannelSetPosition(stream, Bass.ChannelSeconds2Bytes(stream, position)), "Seek sample");
                BassMix.ChannelRemoveFlag(stream, BassFlags.MixerChanPause);
            }
            else
            {
                Check(Bass.ChannelSetPosition(stream, Bass.ChannelSeconds2Bytes(stream, position)), "Seek sample");
                Check(Bass.ChannelPlay(stream), "Play sample");
            }
        }
        public void Stop()
        {
            if (disposed || stream == 0) return;
            if (mixed) BassMix.ChannelAddFlag(stream, BassFlags.MixerChanPause);
            else Bass.ChannelStop(stream);
            Bass.ChannelSetPosition(stream, 0);
        }
        static void Free(int handle)
        {
            if (handle == 0) return;
            Bass.StreamFree(handle); System.Threading.Interlocked.Decrement(ref liveStreams);
        }
        public void Dispose()
        {
            lock (AudioEngine.DeviceLock)
            {
                if (disposed) return;
                Stop(); IsDisposed = true;
                samples.Remove(this);
                if (resampler != 0) BassMix.MixerRemoveChannel(resampler);
                Free(resampler); Free(stream);
                if (decode != stream) Free(decode);
                stream = decode = resampler = 0;
                if (data.IsAllocated) data.Free();
            }
        }
#elif UNITY_WEBGL && !UNITY_EDITOR
        // Web Audio voice over a shared, browser-decoded buffer. Speed changes playbackRate, so pitch follows it.
        int voice;
        public bool Playing => !IsDisposed && WebAudio.OurTaikoVoiceInfo(voice, 0) != 0;
        public double Position => IsDisposed ? 0 : WebAudio.OurTaikoVoiceInfo(voice, 1);
        public float OutputVolume => IsDisposed ? 0 : (float)WebAudio.OurTaikoVoiceInfo(voice, 3);
        public NativeAudioSample(byte[] encoded, AudioEngine engine, bool normalize = true, bool speedChange = false, int? generation = null)
        {
            Check(engine, generation);
            if (encoded == null || encoded.Length == 0) throw new ArgumentException("Empty audio file");
            int buffer = WebAudio.Decode(encoded);
            // The voice holds its own reference to the buffer.
            try { Attach(buffer, normalize, speedChange); }
            finally { WebAudio.Release(buffer); }
            EncodedBytes = encoded.Length;
        }
        NativeAudioSample(int buffer, bool normalize, bool speedChange) => Attach(buffer, normalize, speedChange);
        // Shares a buffer decoded elsewhere (the embedded song or a cached bundled clip); the caller keeps its reference.
        public static NativeAudioSample FromWebBuffer(int buffer, AudioEngine engine, bool normalize = true, bool speedChange = false, int? generation = null)
        {
            Check(engine, generation);
            return new NativeAudioSample(buffer, normalize, speedChange);
        }
        static void Check(AudioEngine engine, int? generation)
        {
            if ((generation.HasValue && generation != engine.Generation) || !engine.Native)
                throw new OperationCanceledException("Audio output changed during preparation");
        }
        void Attach(int buffer, bool normalize, bool speedChange)
        {
            voice = WebAudio.OurTaikoVoiceCreate(buffer, normalize, speedChange);
            if (voice == 0) throw new InvalidOperationException("Web Audio buffer is unavailable");
            Format = "WebAudio";
            lock (AudioEngine.DeviceLock) samples.Add(this);
        }
        // Decoding is asynchronous; a bundled clip reports zero until the browser has decoded it.
        public double Length => IsDisposed ? 0 : WebAudio.OurTaikoVoiceInfo(voice, 2);
        public void SetVolume(float volume) { if (!IsDisposed) WebAudio.OurTaikoVoiceSetVolume(voice, Math.Max(0, volume)); }
        // A delay schedules on the AudioContext clock; otherwise playback starts now.
        public void Play(float volume, bool loop, double position = 0, float speed = 1, double? delay = null)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(NativeAudioSample));
            WebAudio.OurTaikoVoicePlay(voice, Math.Max(0, volume), loop, position, speed, delay ?? 0, delay.HasValue);
        }
        public void Stop() { if (!IsDisposed) WebAudio.OurTaikoVoiceStop(voice); }
        public void Dispose()
        {
            lock (AudioEngine.DeviceLock)
            {
                if (IsDisposed) return;
                IsDisposed = true;
                samples.Remove(this);
                if (voice != 0) WebAudio.OurTaikoVoiceFree(voice);
                voice = 0;
            }
        }
#else
        public float OutputVolume => 0;
        public void SetVolume(float volume) { }
        public bool Playing => false;
        public double Position => 0;
        public NativeAudioSample(byte[] encoded, AudioEngine engine, bool normalize = true, bool speedChange = false, int? generation = null) => throw new PlatformNotSupportedException();
        public void Play(float volume, bool loop, double position = 0, float speed = 1, double? delay = null) { }
        public void Stop() { }
        public void Dispose() { }
#endif
    }
}
