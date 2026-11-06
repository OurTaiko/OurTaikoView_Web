using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Nijiiro Scripts/global/nameplate.lua on the 408x96 plate canvas. The children are drawn in the
    // original's order: plate, title band, band outline, dan chip (its ring, then its text), 1P/2P
    // badge, title, name. The 1P
    // plate follows PlayerInfoController, so an info change updates every plate on screen; the 2P
    // plate follows its SecondInfo (2P's account, else the "2P" guest plate) with the 2p badge.
    [RequireComponent(typeof(RectTransform), typeof(ClipSampler))]
    public sealed class NameplateView : MonoBehaviour
    {
        public Image shadow, bandUnder, band, outline, danBackground, danRing, dan, badge;
        public TMP_Text title, playerName;
        [Tooltip("frame_top: the 5 title backgrounds.")]
        public Sprite[] titleBackgrounds;
        // The 段位 emblem is two layers, like a song's crown: dan_ring_{silver,gold,rainbow} under the
        // rank, by the pass's combo (銀粋 / 金粋 full combo / 虹粋 all 良), then dan_text[_gold]'s rank
        // (初級 .. 達人, 0..5 blank), white for a red pass and gold for a gold pass.
        [Tooltip("dan_ring_silver, dan_ring_gold, dan_ring_rainbow: PlayerInfo.danCombo 0..2.")]
        public Sprite[] danRings;
        [Tooltip("dan_text / dan_text_gold: 初級 .. 達人.")]
        public Sprite[] danTexts, goldDanTexts;
        [Tooltip("0 = 1P, 1 = 2P.")]
        public int seat;
        [Tooltip("global/nameplate/2p: the badge of a 2P plate.")]
        public Sprite secondBadge;

        public PlayerInfo Info { get; private set; }
        public int RainbowFrame { get; private set; }
        public RectTransform RectTransform => (RectTransform)transform;

        PlayerInfoController controller;
        double rainbowStart;
        float renderedCanvasScale;

        void OnEnable()
        {
            controller = PlayerInfoController.EnsureInstance();
            if (seat == 0) controller.Changed += Show;
            else controller.SecondChanged += Show;
            rainbowStart = GameTimeline.FrameTime;
            renderedCanvasScale = float.NaN;
            Canvas.willRenderCanvases += RefreshCanvasScale;
            // No rainbow sample yet: the Animator may not have woken, and evaluating its graph
            // asserts m_DidAwake. Update draws the band before the first render.
            Apply(seat == 0 ? controller.Info : controller.SecondInfo);
        }

        void OnDisable()
        {
            if (controller != null) { controller.Changed -= Show; controller.SecondChanged -= Show; }
            Canvas.willRenderCanvases -= RefreshCanvasScale;
        }

        void RefreshCanvasScale()
        {
            var canvas = playerName.canvas;
            if (canvas == null || Mathf.Approximately(renderedCanvasScale, canvas.scaleFactor)) return;
            renderedCanvasScale = canvas.scaleFactor;
            // CanvasScaler runs in preWillRenderCanvases. TMP's incremental scale update only
            // accounts for lossyScale, missing the cancelling Canvas factor in Overlay mode.
            // Rebuild on first render / Canvas resize only, after that factor has settled.
            playerName.ForceMeshUpdate();
            if (title.isActiveAndEnabled) title.ForceMeshUpdate();
        }

        // The rainbow band runs on real time, like the original's current_ms (it keeps cycling in pause).
        void Update() => ShowRainbow(GameTimeline.FrameTime - rainbowStart);

        // Top-left of the plate canvas, in its stage-anchored parent's skin pixels.
        public void Place(float x, float y)
        {
            var rect = RectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(NameplateLayout.Width, NameplateLayout.Height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        public void Show(PlayerInfo info)
        {
            Apply(info);
            ShowRainbow(GameTimeline.FrameTime - rainbowStart);
        }

        void Apply(PlayerInfo info)
        {
            Info = info = info ?? new PlayerInfo();
            bool hasBand = !info.IsCoin;
            band.enabled = outline.enabled = hasBand;
            bandUnder.enabled = false;
            if (hasBand && !info.rainbow) band.sprite = titleBackgrounds[info.TitleFrame];
            RainbowFrame = 0;

            // *_dani labels only exist in the band family.
            bool hasDan = info.HasDan && hasBand;
            danBackground.enabled = danRing.enabled = dan.enabled = hasDan;
            if (hasDan)
            {
                danRing.sprite = danRings[info.DanComboTier];
                dan.sprite = (info.gold ? goldDanTexts : danTexts)[info.dan];
            }
            badge.enabled = true;
            if (seat == 1 && secondBadge != null) badge.sprite = secondBadge;

            // Np_coin clears the title outright.
            title.gameObject.SetActive(hasBand && info.HasTitle);
            if (title.gameObject.activeSelf)
            {
                SetText(title, info.title, NameplateLayout.TitleFontSize,
                    NameplateLayout.TitleX, NameplateLayout.TitleY, NameplateLayout.TitleBoxWidth);
            }

            var box = NameplateLayout.NameBox(info);
            playerName.fontSize = box.FontSize;
            SetText(playerName, info.name, box.FontSize, box.X, box.Y, NameplateLayout.NameBoxWidth);
        }

        // NameplateRainbow.anim: global animation 12 (texture_change), frame_top_rainbow 0-5 for 50 ms
        // each, looping every 300 ms; frames after the first are drawn over the previous one.
        // nameplate.lua never starts it, so the original stays on frame 0; the plate is meant to cycle.
        void ShowRainbow(double elapsed)
        {
            if (Info == null || !Info.rainbow || Info.IsCoin) return;
            var sampler = GetComponent<ClipSampler>();
            double t = System.Math.Max(0, elapsed) % sampler.clip.length;
            RainbowFrame = System.Math.Min(5, (int)(t / 0.05));
            sampler.Sample(t);
        }

        // draw_in_box: centred in the EditText box and squeezed horizontally, never shrunk, to its width.
        static void SetText(TMP_Text text, string value, float fontSize, float x, float y, float boxWidth)
        {
            text.fontSize = fontSize;
            text.characterSpacing = NameplateLayout.TextSpacing * 100 / fontSize;
            text.text = value ?? "";
            text.rectTransform.Center(x, y);
            // preferredWidth measures without rendering. Leave mesh generation until CanvasScaler
            // has settled: forcing it during a saved scene's OnEnable makes TMP apply the initial
            // Canvas scale twice to the SDF, washing the black outline out into a grey rectangle.
            text.Squeeze(boxWidth);
        }
    }
}
