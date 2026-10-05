using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace OurTaiko
{
    // The two exported frame tables are shared across ranks and read as data, never executed as Lua.
    public sealed class ScoreRankAnimation : MonoBehaviour
    {
        public TextAsset timeline, kiwamiTimeline;
        int rank;
        Sprite rankSprite;
        public UnityEngine.UI.Image[] layers;
        public int[] shapeIds;
        public Sprite[] sprites;
        public Material additive;
        static readonly int[] RankShapes = { 74, 76, 78, 80, 82, 84, 86 };
        static readonly Dictionary<string, Frames> Cache = new Dictionary<string, Frames>();
        Frames data;
        int lastFrame = -1;
        public int FrameCount => Data.FramesByIndex.Length;

        public sealed class Frames
        {
            public readonly Dictionary<int, float[]> Shapes = new Dictionary<int, float[]>();
            public float[][][] FramesByIndex;
            public static Frames Parse(string text)
            {
                var result = new Frames();
                var number = new Regex(@"-?\d+(?:\.\d+)?");
                float[] Numbers(string value) => number.Matches(value).Cast<Match>()
                    .Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
                foreach (Match match in Regex.Matches(text, @"\[(\d+)\]\s*=\s*\{([^}]+)\}"))
                    result.Shapes[int.Parse(match.Groups[1].Value)] = Numbers(match.Groups[2].Value);
                string frameText = text.Substring(text.IndexOf("frames = {", StringComparison.Ordinal));
                result.FramesByIndex = frameText.Split('\n').Where(line => line.TrimStart().StartsWith("{"))
                    .Select(line => Regex.Matches(line, @"\{([^{}]+)\}").Cast<Match>()
                        .Select(m => Numbers(m.Groups[1].Value)).ToArray()).ToArray();
                return result;
            }
        }

        Frames Data
        {
            get
            {
                if (data != null) return data;
                var source = rank == 7 ? kiwamiTimeline : timeline;
                if (!Cache.TryGetValue(source.name, out data)) Cache[source.name] = data = Frames.Parse(source.text);
                return data;
            }
        }

        public void Show(int value, Sprite sprite, double seconds)
        {
            if (rank != value) { rank = value; lastFrame = -1; data = null; }
            rankSprite = sprite;
            if (rank == 0)
            {
                foreach (var layer in layers) layer.enabled = false;
                return;
            }
            Sample(seconds);
        }

        void Sample(double seconds)
        {
            var frames = Data;
            int frame = seconds < 0 ? frames.FramesByIndex.Length - 1
                : Math.Min(frames.FramesByIndex.Length - 1, (int)(seconds * 60));
            if (frame == lastFrame) return;
            lastFrame = frame;
            var rows = frames.FramesByIndex[frame];
            for (int i = 0; i < layers.Length; i++)
            {
                var image = layers[i];
                image.enabled = i < rows.Length;
                if (!image.enabled) continue;
                var row = rows[i];
                int shape = Array.IndexOf(RankShapes, (int)row[0]) >= 0 ? RankShapes[rank - 1] : (int)row[0];
                var size = frames.Shapes[shape];
                image.sprite = Array.IndexOf(RankShapes, shape) >= 0 ? rankSprite : sprites[Array.IndexOf(shapeIds, shape)];
                image.material = row.Length > 7 && row[7] == 8 ? additive : null;
                image.color = new Color(1, 1, 1, row[6]);
                var rect = image.rectTransform;
                rect.pivot = new Vector2(size[2] / size[0], 1 - size[3] / size[1]);
                rect.sizeDelta = new Vector2(size[0], size[1]);
                rect.anchoredPosition = new Vector2(row[1], -row[2]) + (rank == 7 ? new Vector2(17, -5) : Vector2.zero);
                rect.localScale = new Vector3(row[3], row[4], 1);
                rect.localRotation = Quaternion.Euler(0, 0, -row[5]);
            }
        }
    }
}
