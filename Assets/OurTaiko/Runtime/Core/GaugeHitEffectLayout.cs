namespace OurTaiko
{
    // GaugeHitEffect: the burst on the soul badge when a flying note lands. Its timing is
    // Generated/Clips/GaugeHitEffect.anim; only the position is laid out in code.
    public static class GaugeHitEffectLayout
    {
        // Burst and note centre: hit_effect (1834,-30) drawn centred, the note at
        // gauge_hit_effect_note (1738,-126) plus half a note. Lane-local, Y down.
        public const double CentreX = 1834, CentreY = -30;
        // gauge/hit_effect index 1: 2P's badge, under its lane.
        public const double CentreY2P = 300;
        public static double CentreYOf(int seat) => seat == 1 ? CentreY2P : CentreY;
    }
}
