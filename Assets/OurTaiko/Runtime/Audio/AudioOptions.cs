using System;

namespace OurTaiko
{
    public enum AudioBackend { Automatic, Bass, Wasapi, Asio, Unity, WebAudio }

    [Serializable]
    public sealed class AudioOptions
    {
        public AudioBackend backend = AudioBackend.Automatic;
        public int sampleRate = 44100;
        public SoundVolumes volume = new SoundVolumes();
        public int updatePeriodMs = 100, playbackBufferMs = 1000;
        // MajdataPlay's mobile defaults. Desktop uses a 16 ms device period / 64 ms buffer.
        public int devicePeriodMs = 0;
        public int deviceBufferMs = 0;
        public bool androidAAudio = true;
        public bool wasapiExclusive = true;
        public bool wasapiRaw = true;
        public bool wasapiAsync = true;
        public float wasapiBufferSeconds = 0.02f;
        public float wasapiPeriodSeconds = 0.005f;
        public int asioDevice;
        public int asioBufferSamples;

        public bool SameDeviceSettings(AudioOptions b) => b != null && backend == b.backend && sampleRate == b.sampleRate
            && updatePeriodMs == b.updatePeriodMs && playbackBufferMs == b.playbackBufferMs
            && devicePeriodMs == b.devicePeriodMs && deviceBufferMs == b.deviceBufferMs && androidAAudio == b.androidAAudio
            && wasapiExclusive == b.wasapiExclusive && wasapiRaw == b.wasapiRaw && wasapiAsync == b.wasapiAsync
            && wasapiBufferSeconds == b.wasapiBufferSeconds && wasapiPeriodSeconds == b.wasapiPeriodSeconds
            && asioDevice == b.asioDevice && asioBufferSamples == b.asioBufferSamples;

        public int Rate => sampleRate >= 8000 && sampleRate <= 192000 ? sampleRate : 44100;
        public int Period(bool mobile) => Math.Clamp(devicePeriodMs == 0 ? (mobile ? 8 : 16) : devicePeriodMs, 1, 100);
        public int Buffer(bool mobile) => Math.Clamp(deviceBufferMs == 0 ? (mobile ? 32 : 64) : deviceBufferMs, Period(mobile) * 2, 1000);
    }
}
