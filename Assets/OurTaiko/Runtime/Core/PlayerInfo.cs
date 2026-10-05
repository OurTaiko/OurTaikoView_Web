using System;
using UnityEngine;

namespace OurTaiko
{
    // The local player's nameplate data (scores.h PlayerData username / title / title_bg / dan / gold /
    // rainbow), stored as player.json by PlayerInfoController.
    [Serializable]
    public sealed class PlayerInfo
    {
        // scores.cpp seeds player 1 as ('Don-chan', 'Donder Debut!'); nameplate.lua treats that title as unset.
        public const string DefaultName = "Don-chan", DefaultTitle = "Donder Debut!";
        // dan 0 = 初級 .. 24 = 達人 (dan_emblem's 25 crops); frame_top bakes 5 title backgrounds.
        public const int DanCount = 25, TitleBackgroundCount = 5;

        public string name = DefaultName;
        public string title = DefaultTitle;
        public int titleBackground;
        public int dan = -1;      // -1 = no 段位
        public bool gold;         // gold dan emblem
        public bool rainbow;      // rainbow title band

        public bool HasTitle => !string.IsNullOrEmpty(title) && title != DefaultTitle;
        // An out-of-range dan is "no dan", as nameplate.lua's clamp does.
        public bool HasDan => dan >= 0 && dan < DanCount;
        // Np_coin: no title and no dan. The coin plate never shows a title band or dan chip.
        public bool IsCoin => !HasTitle && !HasDan;
        public int TitleFrame => titleBackground >= 0 && titleBackground < TitleBackgroundCount ? titleBackground : 0;

        public PlayerInfo Clone() => (PlayerInfo)MemberwiseClone();

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
