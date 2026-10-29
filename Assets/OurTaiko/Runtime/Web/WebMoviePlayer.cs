using UnityEngine;
using UnityEngine.Video;

namespace OurTaiko
{
    // MoviePlayer on Unity's VideoPlayer. In the browser build it plays the blob URL of the movie the
    // parent page transferred through an HTML <video> (the browser decodes the H.264 MP4 that
    // Fanmade stores); MajdataPlay's FFmpeg player, which the game uses, does not run on WebGL.
    // The Editor plays local paths the same way (tests).
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class WebMoviePlayer : MoviePlayer
    {
        public VideoPlayer video;
        // A seek that never reports back (an unseekable source) stops waiting after this long.
        const float SeekPatience = 2;
        bool open, prepared, seeking, ended;
        float seekStarted;
        Texture shown;

        void Awake()
        {
            if (video == null) video = GetComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.isLooping = false;
            video.source = VideoSource.Url;
            video.renderMode = VideoRenderMode.APIOnly;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.waitForFirstFrame = true;
            video.skipOnDrop = true;
            video.prepareCompleted += OnPrepared;
            video.errorReceived += OnError;
            video.seekCompleted += OnSeeked;
            video.loopPointReached += OnEnded;
        }

        void OnDestroy()
        {
            if (video == null) return;
            video.prepareCompleted -= OnPrepared;
            video.errorReceived -= OnError;
            video.seekCompleted -= OnSeeked;
            video.loopPointReached -= OnEnded;
        }

        void Update()
        {
            if (seeking && Time.realtimeSinceStartup - seekStarted > SeekPatience) seeking = false;
            // The texture can be replaced (a size change); pass the new one on.
            if (prepared && video.texture != shown) { shown = video.texture; ShowTexture(shown); }
        }

        public override bool IsOpen => open;
        public override bool IsPrepared => prepared;
        public override bool IsSeeking => seeking;
        public override bool IsPlaying => prepared && video.isPlaying;
        public override bool IsEnded => ended;
        public override bool CanSeek => prepared && video.canSetTime;
        public override double Position => prepared ? video.time : 0;
        public override double Length => prepared ? video.length : 0;
        public override float Rate
        {
            get => video.playbackSpeed;
            set { if (video.canSetPlaybackSpeed) video.playbackSpeed = value; }
        }

        public override void Open(string path)
        {
            Close();
            open = true;
            video.url = path;
            video.Prepare();
        }

        public override void Close()
        {
            if (open) video.Stop();
            open = prepared = seeking = ended = false;
            video.url = "";
            if (shown != null) { shown = null; ShowTexture(null); }
        }

        public override void Play()
        {
            if (!prepared) return;
            ended = false;
            video.Play();
        }

        public override void Pause() { if (prepared) video.Pause(); }

        public override void Seek(double seconds)
        {
            if (!CanSeek) return;
            ended = false;
            seeking = true;
            seekStarted = Time.realtimeSinceStartup;
            video.time = seconds;
        }

        void OnPrepared(VideoPlayer _)
        {
            if (!open) return;
            prepared = true;
            video.Pause();
            shown = video.texture;
            ShowTexture(shown);
        }

        void OnSeeked(VideoPlayer _) => seeking = false;
        void OnEnded(VideoPlayer _) => ended = true;

        void OnError(VideoPlayer _, string message)
        {
            open = prepared = seeking = false;
            Fail(message);
        }
    }
}
