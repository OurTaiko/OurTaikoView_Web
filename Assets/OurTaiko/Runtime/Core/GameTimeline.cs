using System.Diagnostics;
using UnityEngine;

namespace OurTaiko
{
    // One time source for the application. UI keeps running while a SongClock is paused.
    // Frame values never change within a Unity frame; live time is only for audio scheduling
    // and elapsed-time measurements, never for judging individual input events.
    public static class GameTimeline
    {
        static long origin = Stopwatch.GetTimestamp();
        static int sampledFrame = -1;
        static double frameTime, audioFrameTime;

        public static double Realtime => (Stopwatch.GetTimestamp() - origin) / (double)Stopwatch.Frequency;
        public static double AudioNow => AudioEngine.EnsureInstance().Native ? Realtime : AudioSettings.dspTime;
        public static double FrameTime
        {
            get
            {
                // Editor previews must not create the runtime audio engine.
                if (!Application.isPlaying) return Time.realtimeSinceStartupAsDouble;
                UpdateFrame();
                return frameTime;
            }
        }
        public static double AudioFrameTime
        {
            get { UpdateFrame(); return audioFrameTime; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            origin = Stopwatch.GetTimestamp();
            sampledFrame = -1;
            frameTime = audioFrameTime = 0;
        }

        internal static void UpdateFrame()
        {
            if (sampledFrame == Time.frameCount) return;
            sampledFrame = Time.frameCount;
            frameTime = Realtime;
            audioFrameTime = AudioEngine.EnsureInstance().Native ? frameTime : AudioSettings.dspTime;
        }
    }
}
