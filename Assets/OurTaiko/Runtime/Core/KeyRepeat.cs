namespace OurTaiko
{
    // FDK CCounter.KeyIntervalFunc: a held key fires at once, again after 200 ms, then every 30 ms.
    // Tick returns whether the key fires this frame; a release (or a frame it is not asked) resets it.
    public sealed class KeyRepeat
    {
        public const double SecondMs = 200, LaterMs = 30;

        enum Stage { First, Second, Later }

        Stage stage;
        double firedAt;

        public bool Tick(bool pressed, double nowMs)
        {
            if (!pressed) { stage = Stage.First; return false; }
            switch (stage)
            {
                case Stage.First:
                    stage = Stage.Second; firedAt = nowMs; return true;
                case Stage.Second:
                    if (nowMs - firedAt <= SecondMs) return false;
                    stage = Stage.Later; firedAt = nowMs; return true;
                default:
                    if (nowMs - firedAt <= LaterMs) return false;
                    firedAt = nowMs; return true;
            }
        }

        public void Reset() => stage = Stage.First;
    }
}
