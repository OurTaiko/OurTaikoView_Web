using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The same authored AnimationClip / Playables path as the other play effects.
    [RequireComponent(typeof(ClipSampler))]
    public sealed class EndingView : MonoBehaviour
    {
        public AnimationClip fail, clear, fullCombo, donderful;
        public AudioClip failSound, clearSound, fullComboSound, donderfulSound;
        public AudioClip fullComboVoice, donderfulVoice;
        public AudioSource audioSource;

        // 段位道場 (the cabinet's course ending): the clear clip with 合格 in place of クリア成功, and
        // the fail clip with 不合格 in place of クリア失敗. Nijiiro has no such text, so it is drawn in
        // the same style; the dancers, sparkles and timing stay the clips' own.
        [Header("段位道場")]
        [Tooltip("The clear clip's text (each character, its shadow, the whole word and its white flash), hidden for 合格.")]
        public Sprite[] clearText;
        [Tooltip("The fail clip's クリア失敗 frames: the first (504x144) and the sinking ones (528x192).")]
        public Sprite failText;
        public Sprite[] failTextLow;
        [Tooltip("合 and 格, the whole word, and its white flash.")]
        public Sprite[] passCharacters;
        public Sprite passWord, passFlash;
        [Tooltip("不合格 in the fail frames' two sizes.")]
        public Sprite danFail, danFailLow;
        [Tooltip("Under the clip's layers: the two characters, the whole word and the flash over it.")]
        public Image[] danCharacters;
        public Image danWord, danFlash;

        // The clear clip's characters (クリア成功): each from 26 frames in, the next three later,
        // fading in over 9 frames while it hops (anim y, 4.25 up to 22.5 and back by +19 frames);
        // the white silhouette fades in from 66 and the whole word replaces them at 78, the white
        // fading off it by 89. 合 and 格 land 152 px apart about the word's centre, drawn 1.15 times
        // the clip's character size, as large as the cabinet's 合格.
        const double FrameMs = 1000.0 / 60, CharacterFrom = 26, CharacterStep = 3, WordAt = 78, FlashFrom = 66, FlashTo = 89;
        const float TextScale = 1.15f;
        static readonly float[] CharacterX = { -66 * TextScale, 66 * TextScale };
        static readonly (double Frame, double Y, double Alpha)[] Hop =
        {
            (0, 4.25, 0.19), (1, 8.05, 0.36), (2, 11.4, 0.51), (3, 14.35, 0.64), (4, 16.8, 0.75), (5, 18.85, 0.84),
            (6, 20.4, 0.91), (7, 21.55, 0.96), (8, 22.2, 0.99), (9, 22.5, 1), (10, 22.2, 1), (11, 21.6, 1), (12, 20.4, 1),
            (13, 18.85, 1), (16, 11.4, 1), (19, 0, 1),
        };

        ClipSampler sampler;
        bool dan, danPassed;
        HashSet<Sprite> hidden;
        Image[] layers;
        // The layers hidden last frame: the clip may move a dancer into one without re-enabling it.
        readonly List<Image> hiddenLayers = new List<Image>();
        public double Duration => sampler != null && sampler.clip != null ? sampler.clip.length : 0;

        public void Begin(PlayResult result)
        {
            dan = false;
            foreach (var layer in hiddenLayers) if (layer != null) layer.enabled = true;
            hiddenLayers.Clear();
            ShowDanText(false);
            sampler = GetComponent<ClipSampler>();
            gameObject.SetActive(true);
            var crown = result.ResultCrown;
            sampler.Play(crown == Crown.DonderfulCombo ? donderful : crown == Crown.FullCombo ? fullCombo
                : crown == Crown.Clear ? clear : fail, 0);
            var sound = crown == Crown.DonderfulCombo ? donderfulSound : crown == Crown.FullCombo ? fullComboSound
                : crown == Crown.Clear ? clearSound : failSound;
            if (audioSource != null && sound != null) audioSource.PlayAudioOneShot(sound);
            var voice = crown == Crown.DonderfulCombo ? donderfulVoice : crown == Crown.FullCombo ? fullComboVoice : null;
            if (audioSource != null && voice != null) audioSource.PlayAudioOneShot(voice, AudioGroup.Voice);
        }

        // A course's ending: 合格 on the clear clip, or 不合格 on the fail clip, with their sounds.
        public void BeginDan(bool passed)
        {
            sampler = GetComponent<ClipSampler>();
            gameObject.SetActive(true);
            dan = true; danPassed = passed;
            hidden ??= new HashSet<Sprite>(clearText ?? Array.Empty<Sprite>());
            layers ??= GetComponentsInChildren<Image>(true);
            sampler.Play(passed ? clear : fail, 0);
            var sound = passed ? clearSound : failSound;
            if (audioSource != null && sound != null) audioSource.PlayAudioOneShot(sound);
            ShowTime(0);
        }

        public void ShowTime(double seconds)
        {
            sampler.Sample(Math.Min(Math.Max(0, seconds), Duration));
            if (dan) ShowDan(seconds * 1000 / FrameMs);
        }

        void ShowDan(double f)
        {
            // The clip's own text: hidden for 合格, swapped for 不合格.
            foreach (var layer in hiddenLayers) if (layer != null) layer.enabled = true;
            hiddenLayers.Clear();
            foreach (var layer in layers)
            {
                if (layer == null || Array.IndexOf(danCharacters, layer) >= 0 || layer == danWord || layer == danFlash) continue;
                if (danPassed)
                {
                    if (layer.enabled && layer.sprite != null && hidden.Contains(layer.sprite)) { layer.enabled = false; hiddenLayers.Add(layer); }
                }
                else if (layer.sprite == failText) layer.sprite = danFail;
                else if (layer.sprite != null && failTextLow != null && Array.IndexOf(failTextLow, layer.sprite) >= 0) layer.sprite = danFailLow;
            }
            ShowDanText(danPassed);
            if (!danPassed) return;
            bool word = f >= WordAt;
            for (int i = 0; i < danCharacters.Length; i++)
            {
                var character = danCharacters[i];
                double since = f - (CharacterFrom + CharacterStep * i);
                bool shown = !word && since >= 0;
                character.enabled = shown;
                if (!shown) continue;
                var (y, alpha) = HopAt(since);
                character.rectTransform.anchoredPosition = new Vector2(CharacterX[i], (float)y);
                character.rectTransform.localScale = Vector3.one * TextScale;
                character.color = new Color(1, 1, 1, (float)alpha);
            }
            danWord.enabled = word;
            danWord.rectTransform.localScale = danFlash.rectTransform.localScale = Vector3.one * TextScale;
            double flash = f < FlashFrom ? 0 : f < WordAt ? (f - FlashFrom) / (WordAt - FlashFrom) : f < FlashTo ? 1 - (f - WordAt) / (FlashTo - WordAt) : 0;
            danFlash.enabled = flash > 0;
            danFlash.color = new Color(1, 1, 1, (float)flash);
        }

        static (double Y, double Alpha) HopAt(double f)
        {
            if (f >= Hop[Hop.Length - 1].Frame) return (0, 1);
            for (int i = 0; i < Hop.Length - 1; i++)
            {
                if (f > Hop[i + 1].Frame) continue;
                double u = (f - Hop[i].Frame) / (Hop[i + 1].Frame - Hop[i].Frame);
                return (Hop[i].Y + (Hop[i + 1].Y - Hop[i].Y) * u, Hop[i].Alpha + (Hop[i + 1].Alpha - Hop[i].Alpha) * u);
            }
            return (Hop[0].Y, Hop[0].Alpha);
        }

        void ShowDanText(bool shown)
        {
            if (danCharacters != null) foreach (var character in danCharacters) if (character != null) character.gameObject.SetActive(shown);
            if (danWord != null) danWord.gameObject.SetActive(shown);
            if (danFlash != null) danFlash.gameObject.SetActive(shown);
        }
    }
}
