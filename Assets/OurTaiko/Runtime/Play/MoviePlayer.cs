using System;
using UnityEngine;

namespace OurTaiko
{
    // What BackgroundMovieView needs from a video decoder. The game uses MajdataPlay's FFmpeg
    // player (FFmpegMoviePlayer); OurTaikoView_Web, where FFmpeg does not run, the browser's
    // decoder through Unity's VideoPlayer. BackgroundMovieView and MovieSync are the same file in both.
    public abstract class MoviePlayer : MonoBehaviour
    {
        // Something is open (preparing, prepared, playing or failed).
        public abstract bool IsOpen { get; }
        // The first frame is ready: Position, Length and seeking are valid.
        public abstract bool IsPrepared { get; }
        public abstract bool IsSeeking { get; }
        public abstract bool IsPlaying { get; }
        // Played to its end; the last frame stays.
        public abstract bool IsEnded { get; }
        public abstract bool CanSeek { get; }
        // Seconds into the movie, and its length (0 when unknown).
        public abstract double Position { get; }
        public abstract double Length { get; }
        public abstract float Rate { get; set; }

        // The frame texture, null once closed; and a failure to open or play.
        public event Action<Texture> TextureChanged;
        public event Action<string> Failed;
        protected void ShowTexture(Texture texture) => TextureChanged?.Invoke(texture);
        protected void Fail(string message) => Failed?.Invoke(message);

        // Opens a movie (a path or URL) and prepares its first frame, paused.
        public abstract void Open(string path);
        public abstract void Close();
        public abstract void Play();
        public abstract void Pause();
        // Seeks, playing afterwards when it was playing (or Play is called meanwhile).
        public abstract void Seek(double seconds);
    }
}
