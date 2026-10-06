using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
using ManagedBass;
using ManagedBass.Mix;
#endif
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
using System.Runtime.InteropServices;
using ManagedBass.Wasapi;
using ManagedBass.Asio;
using AOT;
#endif

namespace OurTaiko
{
    // One native output for the application. Scene AudioSources retain their authored clip/volume,
    // but all playback goes through AudioBus; Unity only outputs when the fallback is selected.
    public sealed class AudioEngine : MonoBehaviour
    {
        public static AudioEngine Instance { get; private set; }
        public AudioBackend Backend { get; private set; } = AudioBackend.Unity;
        public string Diagnostics { get; private set; }
        public bool Native => Backend != AudioBackend.Unity;
        public int Mixer { get; private set; }
        public float[,] MixingMatrix { get; private set; }
        AudioOptions appliedOptions;
        public bool HasPendingDeviceChanges => appliedOptions != null && !appliedOptions.SameDeviceSettings(SettingManager.EnsureInstance().Settings.audio);
        internal static readonly object DeviceLock = new object();
        public int Generation { get; private set; }
        bool applying;
        bool initialized;
        bool nativeInitialized;
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        static readonly WasapiProcedure WasapiCallback = ReadWasapi;
        bool wasapiInitialized, asioInitialized;
        static int callbackMixer;
        [MonoPInvokeCallback(typeof(WasapiProcedure))]
        static int ReadWasapi(IntPtr buffer, int length, IntPtr user)
        {
            int mixer = System.Threading.Volatile.Read(ref callbackMixer);
            return mixer == 0 ? 0 : Math.Max(0, Bass.ChannelGetData(mixer, buffer, length));
        }
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() => EnsureInstance();
        public static AudioEngine EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<AudioEngine>();
            if (existing != null) { existing.Initialize(); return existing; }
            return new GameObject(nameof(AudioEngine)).AddComponent<AudioEngine>();
        }
        void Awake() => Initialize();
        void Initialize()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (initialized) return;
            initialized = true;
            DontDestroyOnLoad(gameObject);
            appliedOptions = SettingManager.EnsureInstance().Settings.Clone().audio;
            InitializeOutput(appliedOptions, true);
        }
        void InitializeOutput(AudioOptions options, bool allowFallback)
        {
            Backend = AudioBackend.Unity;
            string failure = null;
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
            if (options.backend != AudioBackend.Unity)
            {
                try
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    Bass.Configure(Configuration.AndroidAAudio, options.androidAAudio);
#endif
                    Bass.Configure(Configuration.UpdatePeriod, Math.Clamp(options.updatePeriodMs, 5, 100));
                    Bass.Configure(Configuration.PlaybackBufferLength, Math.Clamp(options.playbackBufferMs, Math.Clamp(options.updatePeriodMs, 5, 100) + 1, 5000));
                    Bass.Configure(Configuration.DevicePeriod, options.Period(Application.isMobilePlatform));
                    Bass.Configure(Configuration.DeviceBufferLength, options.Buffer(Application.isMobilePlatform));
                    Bass.Configure(Configuration.DevNonStop, true);
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                    if (options.backend != AudioBackend.Bass)
                    {
                        try
                        {
                            if (options.backend == AudioBackend.Asio) InitAsio(options);
                            else InitWasapi(options);
                        }
                        catch (Exception error)
                        {
                            FreeNative();
                            if (!allowFallback && options.backend != AudioBackend.Automatic) throw;
                            failure = error.Message;
                        }
                    }
#endif
#if !UNITY_EDITOR_WIN && !(UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                    if (!allowFallback && (options.backend == AudioBackend.Wasapi || options.backend == AudioBackend.Asio))
                        throw new PlatformNotSupportedException("This audio backend requires Windows");
#endif
                    if (Mixer == 0) InitBass(options);
                    Diagnostics = $"{Backend}; {options.Rate} Hz requested; device period {options.Period(Application.isMobilePlatform)} ms requested; buffer {options.Buffer(Application.isMobilePlatform)} ms requested; stream buffering disabled";
                    if (failure != null) Diagnostics += "; fallback: " + failure;
                }
                catch (Exception error)
                {
                    failure = error.Message;
                    FreeNative();
                    Backend = AudioBackend.Unity;
                    if (!allowFallback) throw;
                }
            }
#elif UNITY_WEBGL
            if (options.backend != AudioBackend.Unity)
            {
                if (WebAudio.Init()) { Backend = AudioBackend.WebAudio; Diagnostics = WebAudio.Describe(); }
                else
                {
                    // Unity audio is disabled in the Web build, so there is no fallback output.
                    failure = "Web Audio is unavailable in this browser";
                    if (!allowFallback) throw new PlatformNotSupportedException(failure);
                }
            }
#else
            if (options.backend != AudioBackend.Unity)
            {
                failure = "Native BASS is unavailable on this platform";
                if (!allowFallback) throw new PlatformNotSupportedException(failure);
            }
#endif
            if (!Native)
            {
                Diagnostics = "Unity audio" + (failure == null ? " (selected)" : "; fallback: " + failure);
                if (failure != null) UnityEngine.Debug.LogWarning("[Audio] " + Diagnostics);
            }
            UnityEngine.Debug.Log("[Audio] " + Diagnostics);
        }
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
        static void Check(bool success, string operation)
        {
            if (!success) throw new InvalidOperationException(operation + ": " + Bass.LastError);
        }
        void InitBass(AudioOptions options)
        {
            Check(Bass.Init(), "BASS device initialization");
            nativeInitialized = true;
            Backend = AudioBackend.Bass;
        }
#endif
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        void InitWasapi(AudioOptions options)
        {
            Check(Bass.Init(Bass.NoSoundDevice, options.Rate), "BASS decode device");
            nativeInitialized = true;
            var combinations = new[] { (true, true), (true, false), (false, true), (false, false) };
            bool attempt = false;
            foreach (var pair in combinations)
            {
                if (pair == (options.wasapiExclusive, options.wasapiRaw)) attempt = true;
                if (!attempt) continue;
                var flags = WasapiInitFlags.EventDriven;
                if (pair.Item1) flags |= WasapiInitFlags.Exclusive | (options.wasapiAsync ? WasapiInitFlags.Async : 0);
                if (pair.Item2) flags |= WasapiInitFlags.Raw;
                if (BassWasapi.Init(-1, 0, 0, flags,
                    pair.Item1 ? Mathf.Clamp(options.wasapiBufferSeconds, 0.005f, 0.5f) : 0,
                    pair.Item1 ? Mathf.Clamp(options.wasapiPeriodSeconds, 0.001f, 0.1f) : 0, WasapiCallback))
                {
                    wasapiInitialized = true;
                    break;
                }
                BassWasapi.Free();
            }
            if (!wasapiInitialized) throw new InvalidOperationException("WASAPI exclusive/shared initialization failed");
            Check(BassWasapi.GetInfo(out var info), "WASAPI format");
            Mixer = BassMix.CreateMixerStream(info.Frequency, info.Channels, BassFlags.Float | BassFlags.Decode | BassFlags.MixerNonStop);
            Check(Mixer != 0, "WASAPI mixer");
            Bass.ChannelSetAttribute(Mixer, ChannelAttribute.Buffer, 0);
            Bass.ChannelSetAttribute(Mixer, (ChannelAttribute)86017, 8);
            MixingMatrix = CreateMixingMatrix(Bass.ChannelGetInfo(Mixer).Channels);
            System.Threading.Volatile.Write(ref callbackMixer, Mixer);
            Check(BassWasapi.Start(), "Start WASAPI");
            Backend = AudioBackend.Wasapi;
        }
        void InitAsio(AudioOptions options)
        {
            Check(Bass.Init(Bass.NoSoundDevice, options.Rate), "BASS decode device");
            nativeInitialized = true;
            if (!BassAsio.Init(options.asioDevice, AsioInitFlags.Thread)) throw new InvalidOperationException("ASIO initialization: " + BassAsio.LastError);
            asioInitialized = true;
            BassAsio.Rate = options.Rate;
            Mixer = BassMix.CreateMixerStream((int)BassAsio.Rate, BassAsio.Info.Outputs, BassFlags.Float | BassFlags.Decode | BassFlags.MixerNonStop);
            Check(Mixer != 0, "ASIO mixer");
            Bass.ChannelSetAttribute(Mixer, ChannelAttribute.Buffer, 0);
            Bass.ChannelSetAttribute(Mixer, (ChannelAttribute)86017, 8);
            MixingMatrix = CreateMixingMatrix(Bass.ChannelGetInfo(Mixer).Channels);
            BassAsio.ChannelEnableBass(false, 0, Mixer, true);
            BassAsio.ChannelSetFormat(false, 0, AsioSampleFormat.Float);
            BassAsio.ChannelJoin(false, 1, 0);
            BassAsio.ChannelSetFormat(false, 1, AsioSampleFormat.Float);
            if (!BassAsio.Start(Math.Max(0, options.asioBufferSamples)))
                throw new InvalidOperationException("ASIO stereo output: " + BassAsio.LastError);
            Backend = AudioBackend.Asio;
        }
#endif
        // Called under the settings scene's closed transition, before the next scene loads.
        // Native preparation holds the same lock; wait without blocking the main thread.
        public async Task ApplyPendingSettingsAsync()
        {
            if (!HasPendingDeviceChanges) return;
            if (applying) throw new InvalidOperationException("Audio settings are already being applied");
            applying = true;
            bool entered = false;
            try
            {
                while (!(entered = Monitor.TryEnter(DeviceLock)))
                    await Awaitable.NextFrameAsync(destroyCancellationToken);
                var manager = SettingManager.EnsureInstance();
                var requested = manager.Settings.Clone().audio;
                var previous = appliedOptions;
                Generation++;
                foreach (var bus in FindObjectsByType<AudioBus>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                { bus.Stop(); bus.Release(); }
                NativeAudioSample.ReleaseAll();
                FreeNative();
                try
                {
                    InitializeOutput(requested, false);
                    appliedOptions = requested;
                }
                catch (Exception error)
                {
                    FreeNative();
                    InitializeOutput(previous, true);
                    // Keep live volume changes while restoring the last usable device configuration.
                    var restored = manager.Settings.Clone();
                    previous.volume = restored.audio.volume;
                    restored.audio = previous;
                    manager.Set(restored);
                    throw new InvalidOperationException("Could not apply audio settings; restored " + Backend + ": " + error.Message, error);
                }
            }
            finally
            {
                if (entered) Monitor.Exit(DeviceLock);
                applying = false;
            }
        }
        public static float[,] CreateMixingMatrix(int channels)
        {
            var matrix = new float[channels, 2];
            if (channels == 1) { matrix[0, 0] = matrix[0, 1] = 0.5f; return matrix; }
            for (int row = 0; row < channels; row++) matrix[row, row % 2] = 1;
            if (channels == 3) { matrix[1, 0] = matrix[1, 1] = 0.5f; matrix[2, 0] = 0; matrix[2, 1] = 1; }
            return matrix;
        }
        void OnApplicationPause(bool paused)
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            if (!Native) return;
            Bass.GlobalMusicVolume = paused ? 0 : 10000;
            Bass.GlobalSampleVolume = paused ? 0 : 10000;
            Bass.GlobalStreamVolume = paused ? 0 : 10000;
#endif
        }
        void FreeNative()
        {
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (wasapiInitialized) { BassWasapi.Stop(); BassWasapi.Free(); wasapiInitialized = false; }
            if (asioInitialized) { BassAsio.Stop(); BassAsio.Free(); asioInitialized = false; }
            System.Threading.Volatile.Write(ref callbackMixer, 0);
#endif
            if (Mixer != 0) { Bass.StreamFree(Mixer); Mixer = 0; }
            if (nativeInitialized) { Bass.Free(); nativeInitialized = false; }
#endif
        }
        void OnDestroy()
        {
            if (Instance != this) return;
            // Release pinned stream memory before unloading the native device, including Play-mode exit.
            foreach (var bus in FindObjectsByType<AudioBus>(FindObjectsInactive.Include, FindObjectsSortMode.None)) bus.Release();
            lock (DeviceLock)
            {
                Generation++;
                NativeAudioSample.ReleaseAll();
                FreeNative();
            }
            Instance = null;
        }
    }
}
