using System;
using UnityEngine;

namespace OurTaiko
{
    // The local player's nameplate data (scores.h PlayerData username / title / title_bg / dan / gold /
    // rainbow, plus danCombo), stored as player.json by PlayerInfoController.
    [Serializable]
    public sealed class PlayerInfo
    {
        // scores.cpp seeds player 1 as ('Don-chan', 'Donder Debut!'); nameplate.lua treats that title as unset.
        public const string DefaultName = "Don-chan", DefaultTitle = "Donder Debut!";
        // dan 0 = 初級 .. 24 = 達人 (the emblem sheets' 25 crops); the game offers 五級 (6) .. 達人 only,
        // so 0..5 count as no 段位 (user decision, 2026-10-22). frame_top bakes 5 title backgrounds.
        public const int DanCount = 25, DanFirst = 6, TitleBackgroundCount = 5;

        public string name = DefaultName;
        public string title = DefaultTitle;
        public int titleBackground;
        public int dan = -1;      // -1 = no 段位
        public bool gold;         // 金合格: gold emblem text (red pass has white text)
        public int danCombo;      // the emblem's ring, as a song's crown: 0 銀粋, 1 金粋 (full combo), 2 虹粋 (all 良)
        public bool rainbow;      // rainbow title band

        public bool HasTitle => !string.IsNullOrEmpty(title) && title != DefaultTitle;
        // An out-of-range dan is "no dan", as nameplate.lua's clamp does.
        public bool HasDan => dan >= DanFirst && dan < DanCount;
        public int DanComboTier => danCombo >= 0 && danCombo <= 2 ? danCombo : 0;
        // Np_coin: no title and no dan. The coin plate never shows a title band or dan chip.
        public bool IsCoin => !HasTitle && !HasDan;
        public int TitleFrame => titleBackground >= 0 && titleBackground < TitleBackgroundCount ? titleBackground : 0;

        public PlayerInfo Clone() => (PlayerInfo)MemberwiseClone();

        // 2P has no account or player file yet (user decision, 2026-10-20): named 2P with the default
        // title, so it wears the plain coin plate like a fresh 1P.
        public static PlayerInfo SecondPlayer => new PlayerInfo { name = "2P" };

        public static PlayerInfo FromJson(string json)
        {
            var info = new PlayerInfo();
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, info);
            info.name ??= "";
            info.title ??= "";
            return info;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);
    }
}
