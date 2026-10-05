using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Entry:draw_background (Nijiiro entry.lua): the street, four twinkles and two lantern glows
    // sampled from anim/entry_bg (a 360-frame loop), and the street-light flash over the first
    // 19 frames only. The other parent layers (tower, shops, people, lights) are not drawn.
    // The images are saved in the scene (EntryView); only alpha and twinkle scale change here.
    public sealed class EntryBackground
    {
        // anim/entry_bg track of each twinkle (EntryView.twinkles order)
        static readonly string[] TwinkleTracks = { "#11@1/#5@0/#3@0", "#11@1/#9@2/#3@0", "#11@1/#8@1/#6@0", "#11@1/#10@3/#6@0" };
        const double StreetLitFrames = 19;

        readonly LumenClip clip;
        readonly double loopFrames;
        readonly Image[] twinkles, glows;
        readonly Image streetLit;

        public EntryBackground(EntryView view, LumenClip clip)
        {
            this.clip = clip;
            loopFrames = Math.Max(1, clip.Last - clip.First + 1);
            twinkles = view.twinkles;
            glows = view.glows;
            streetLit = view.streetLit;
        }

        public void Show(double elapsedMs)
        {
            double frames = elapsedMs * 0.06;
            double f = frames % loopFrames;
            for (int i = 0; i < twinkles.Length; i++)
            {
                string track = TwinkleTracks[i % TwinkleTracks.Length];
                double? scale = clip.Get(track, f, "sx");
                float alpha = (float)(clip.Get(track, f, "a") ?? 0);
                twinkles[i].Alpha(scale.HasValue && alpha > 0.002f ? alpha : 0);
                if (scale.HasValue) twinkles[i].rectTransform.localScale = Vector3.one * (float)scale.Value;
            }
            float glow = (float)clip.Get("#16@3/#14@0", f, "a", 1);
            foreach (var image in glows) image.Alpha(glow);
            // monotonic, not wrapped: the lights come on once
            streetLit.Alpha(frames < StreetLitFrames ? (float)clip.Get("#17@7", frames, "a", 0) : 0);
        }
    }

    // Entry:draw_credit — the arcade credit rows 「１人プレイ」/「２人プレイ」 with
    // 「太鼓をたたいてスタート！」: both blink on anim/credit_row (120-frame loop from frame 5),
    // fade in on anim/credit_fade, and on a join the joined row flashes white for 24 frames before
    // the rows fade out.
    public sealed class EntryCredit
    {
        const double LoopStart = 5, LoopFrames = 120, DecideFlashFrames = 24;

        readonly LumenClip row, fade;
        readonly Image[] pills, flashes;
        readonly TextMeshProUGUI[] labels, messages, highlights;

        public float Alpha { get; private set; }
        public float FlashAlpha { get; private set; }
        public bool IsVisible => Alpha > 0.002f;

        public EntryCredit(EntryView view, LumenClip row, LumenClip fade)
        {
            this.row = row; this.fade = fade;
            pills = view.creditPills; flashes = view.creditFlashes;
            labels = view.creditLabels; messages = view.creditMessages; highlights = view.creditHighlights;
            view.credit.gameObject.SetActive(true);
        }

        // Credit screen: rows fade in on credit_fade frames 5..15 and blink together (arcade model).
        public void ShowWaiting(double msSinceStart, double msSinceBlink)
        {
            double fi = Math.Min(15, 5 + msSinceStart * 0.06);
            double blink = LoopStart + msSinceBlink * 0.06 % LoopFrames;
            Draw((float)fade.Get("player1_text_instance", fi, "a", 1), blink, -1, 0);
        }

        // Entry:draw_credit_decide: flash the joined row (credit_row #8@4 from `choose`), then fade
        // out on credit_fade from frame 16. Returns false once the rows are gone.
        public bool ShowDecided(double msSinceDecide, int joinedRow)
        {
            double t = msSinceDecide * 0.06;
            float flash = (float)row.Get("#8@4", (row.Label("choose") ?? 125) + Math.Min(t, DecideFlashFrames), "a", 0);
            double fadeFrame = 16 + Math.Max(0, t - (DecideFlashFrames + 1));
            float alpha = (float)fade.Get("player1_text_instance", Math.Min(fadeFrame, 35), "a", 0);
            if (alpha <= 0.002f) { Hide(); return false; }
            Draw(alpha, LoopStart, joinedRow, flash);
            return true;
        }

        public void Hide() => Draw(0, LoopStart, -1, 0);

        void Draw(float alpha, double frame, int flashRow, float flash)
        {
            Alpha = alpha;
            FlashAlpha = flashRow >= 0 ? alpha * flash : 0;
            float message = (float)row.Get("text_message_instance", frame, "a", 1);
            float yellow = (float)(row.Get("text_message_instance_2", frame, "cr", 0) / 256.0);
            for (int i = 0; i < pills.Length; i++)
            {
                pills[i].Alpha(alpha);
                labels[i].alpha = alpha;
                messages[i].alpha = alpha * message;
                highlights[i].alpha = alpha * message * yellow;
                labels[i].enabled = alpha > 0.002f;
                messages[i].enabled = alpha * message > 0.002f;
                highlights[i].enabled = alpha * message * yellow > 0.001f;
                flashes[i].Alpha(i == flashRow ? FlashAlpha : 0);
            }
        }
    }

    // One mode board of the Entry list: its saved title and the scene it opens.
    public sealed class EntryMode
    {
        public string Title, Scene;
    }

    // EntryBox:draw (Nijiiro box.lua): the arcade mode list. The selected board sits open at the
    // centre and the others closed in mode_list slots. Three boards remain visible, including
    // at either end of the list; the group shifts within the stage's safe area. A ka slides every board linearly
    // over 9 frames, and the newly selected board only opens (select_on) after the slide while the
    // old one closes (select_off). The first appearance plays `in`: the selected board fades up and
    // opens, the closed boards fly in from three slots out. The cursor glow pulses on cursor_glow,
    // and the decide plays `choose`'s white flash while the list fades out (entry animation 9).
    public sealed class EntryModeList
    {
        const double FrameMs = 1000.0 / 60, SlideFrames = 9, ListInDelay = 10, ListInFrames = 12;

        readonly LumenClip board, glow, list;
        readonly double selectOn, selectOff, inLabel, chooseLabel, onLimit, offLimit, inLimit, chooseLimit, glowFrames;
        readonly Vector2 listIn;
        readonly EntryModeBoard[] boards;
        readonly EntryView view;
        readonly Vector2 authoredRoot;
        readonly Vector2[] authoredPositions;
        Vector2 rootFrom, rootTarget;
        double rootSlideStarted = double.NaN;
        int selected;

        public RectTransform Root { get; }
        public IReadOnlyList<EntryModeBoard> Boards => boards;
        public EntryModeBoard Selected => boards[selected];
        // The selected board's state, as the single-board version reported it.
        public float Openness => Selected.Openness;
        public float Fade => Selected.Fade;
        public float ChooseFlash => Selected.ChooseFlash;

        // The boards are saved at their first layout (the first board selected), so each board's
        // base is its saved position minus that slot; the slides add slot offsets to the base.
        public EntryModeList(EntryView view, LumenClip board, LumenClip glow, LumenClip list)
        {
            this.view = view;
            this.board = board; this.glow = glow; this.list = list;
            selectOn = board.Label("select_on") ?? 27;
            selectOff = board.Label("select_off") ?? 52;
            inLabel = board.Label("in") ?? 67;
            chooseLabel = board.Label("choose") ?? 117;
            onLimit = selectOff - selectOn - 1;
            offLimit = inLabel - selectOff - 1;
            inLimit = chooseLabel - inLabel - 1;
            chooseLimit = board.Last - chooseLabel;
            glowFrames = Math.Max(1, glow.Last - glow.First + 1);
            double listInLabel = list.Label("in") ?? 87;
            listIn = new Vector2((float)list.Get("kanban_3", listInLabel, "tx", 170), (float)list.Get("kanban_3", listInLabel, "ty", 878));
            Root = view.modeBoards;
            authoredRoot = Root.anchoredPosition;
            boards = new EntryModeBoard[view.boards.Length];
            authoredPositions = new Vector2[boards.Length];
            for (int i = 0; i < boards.Length; i++)
            {
                authoredPositions[i] = view.boards[i].root.anchoredPosition;
                boards[i] = new EntryModeBoard(view.boards[i], Slot(i));
            }
        }

        Vector2 Slot(int rel) => Slot(list, rel);

        public static int FirstVisible(int selected, int count) => Math.Max(0, Math.Min(selected - 1, count - 3));

        // Preserve each board's authored adjustment and the skin's slot spacing. Move the group
        // only as far as necessary to keep all three visible hit areas clear of the global chrome.
        public static Vector2 FitPosition(EntryView view, LumenClip list, int selected, Vector2 authoredRoot, IReadOnlyList<Vector2> authoredPositions = null)
        {
            float top = float.PositiveInfinity, bottom = float.NegativeInfinity;
            int first = FirstVisible(selected, view.boards.Length);
            for (int i = first; i < Math.Min(first + 3, view.boards.Length); i++)
            {
                var b = view.boards[i];
                var saved = Slot(list, i);
                var target = Slot(list, i - selected);
                float y = -(authoredRoot.y + (authoredPositions == null ? b.root.anchoredPosition.y : authoredPositions[i].y) + saved.y - target.y + b.hit.rectTransform.anchoredPosition.y);
                float half = (i == selected ? b.openHitSize.y : b.closedHitSize.y) / 2;
                top = Math.Min(top, y - half); bottom = Math.Max(bottom, y + half);
            }
            float height = ((RectTransform)view.modeBoards.parent).rect.height;
            float shift = top < height * view.modeSafeArea.x ? height * view.modeSafeArea.x - top
                : bottom > height * view.modeSafeArea.y ? height * view.modeSafeArea.y - bottom : 0;
            return authoredRoot - Vector2.up * shift;
        }

        // A board's offset (y down) from the open-board slot, rel slots below it (mode_list
        // `wait`): kanban_1 is the selected slot; the slots above/below are kanban_2/3, 4/5, 6/7.
        public static Vector2 Slot(LumenClip list, int rel)
        {
            if (rel == 0) return Vector2.zero;
            int n = Math.Abs(rel);
            string kanban = "kanban_" + (2 * n + (rel < 0 ? 0 : 1));
            double waitLabel = list.Label("wait") ?? 13;
            double? tx = list.Get(kanban, waitLabel, "tx"), ty = list.Get(kanban, waitLabel, "ty");
            if (tx.HasValue && ty.HasValue) return new Vector2((float)tx.Value, (float)ty.Value);
            int sign = rel < 0 ? -1 : 1;    // more slots than the arcade list carries
            return new Vector2(sign * (50 + (n - 1) * 40), sign * (305 + (n - 1) * 191));
        }

        double Open(double frame)
        {
            double sy = board.Get("board_bg_center_instance", frame, "sy", EntryModeBoard.ClosedSy);
            return Math.Max(0, Math.Min(1, (sy - EntryModeBoard.ClosedSy) / (1 - EntryModeBoard.ClosedSy)));
        }

        static double Segment(double label, double u, double limit) => label + Math.Max(0, Math.Min(limit, u));
        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        static double EaseOut(double t) { t = Clamp01(t); return 1 - (1 - t) * (1 - t); }

        // nowMs: real clock; msSinceIn: since the list first appeared; selectedIndex: the flow's
        // selection; fade: entry animation 9 (1 until decided); msSinceChoose: negative before it.
        public void Show(double nowMs, double msSinceIn, int selectedIndex, float fade, double msSinceChoose)
        {
            selected = Math.Max(0, Math.Min(boards.Length - 1, selectedIndex));
            var destination = FitPosition(view, list, selected, authoredRoot, authoredPositions);
            if (double.IsNaN(rootSlideStarted)) { rootFrom = rootTarget = destination; rootSlideStarted = nowMs - SlideFrames * FrameMs; }
            if (destination != rootTarget) { rootFrom = Root.anchoredPosition; rootTarget = destination; rootSlideStarted = nowMs; }
            Root.anchoredPosition = Vector2.Lerp(rootFrom, rootTarget, (float)Clamp01((nowMs - rootSlideStarted) / (SlideFrames * FrameMs)));
            double tIn = msSinceIn / FrameMs;
            bool inRunning = tIn <= inLimit;
            double pulse = glow.Get("#12@0", nowMs / FrameMs % glowFrames, "a", 1);
            for (int i = 0; i < boards.Length; i++)
            {
                var b = boards[i];
                bool isSelected = i == selected;
                // The arcade opens the new board only after the 9-frame slide (mode_select.lua MenuMove).
                if (isSelected != b.WasSelected)
                {
                    b.WasSelected = isSelected;
                    b.OpenStartedAt = nowMs + (isSelected ? SlideFrames * FrameMs : 0);
                    b.Opening = isSelected;
                }
                int rel = i - selected;
                var slot = Slot(rel);
                int first = FirstVisible(selected, boards.Length);
                b.Retarget(nowMs, slot, i >= first && i < first + 3 ? 1 : 0, SlideFrames * FrameMs);
                var position = b.Position;
                if (inRunning && rel != 0)
                {
                    double q = EaseOut((tIn - ListInDelay) / ListInFrames);
                    float sign = rel < 0 ? -1 : 1;
                    position += (float)(1 - q) * (new Vector2(sign * listIn.x, sign * listIn.y) - slot);
                }

                double o, info, cursor;
                float boardFade = fade;
                if (inRunning)
                {
                    double f = Segment(inLabel, tIn, inLimit);
                    o = isSelected ? Open(f) : 0;
                    info = isSelected ? board.Get("text_info_instance", f, "a", 0) : 0;
                    cursor = isSelected ? board.Get("cursor_center", f, "a", 0) : 0;
                    boardFade *= (float)board.Get("board_bg_center_instance", f, "a", 0);
                }
                else
                {
                    double u = double.IsNaN(b.OpenStartedAt) ? 999 : (nowMs - b.OpenStartedAt) / FrameMs;
                    double frame = b.Opening ? Segment(selectOn, u, onLimit) : Segment(selectOff, u, offLimit);
                    o = Open(frame);
                    info = board.Get("text_info_instance", frame, "a", 0);
                    cursor = o;
                }
                float title = inRunning && isSelected ? boardFade * (float)info : boardFade;
                double c = msSinceChoose / FrameMs;
                float flash = isSelected && msSinceChoose >= 0 && c <= chooseLimit ? (float)board.Get("#22@6", chooseLabel + c, "a", 0) : 0;
                b.Draw(position, (float)o, boardFade * b.Visibility, (float)info, (float)(cursor * pulse), title * b.Visibility, flash);
            }
        }
    }

    // One board's saved plates and texts, drawn at a list offset (y down) from its saved slot.
    public sealed class EntryModeBoard
    {
        public const double ClosedSy = 0.1857;

        readonly EntryView.BoardView view;
        readonly Image cursor, closed, open, flash;
        readonly TextMeshProUGUI title;
        readonly TextMeshProUGUI[] info;
        readonly Vector2 basePosition;
        Vector2 from, target;
        float fromVisibility, targetVisibility;
        double slideStartedAt = double.NaN;

        public Image Hit { get; }
        public EntryMode Mode { get; }
        public RectTransform Root { get; }
        public float Openness { get; private set; }
        public float Fade { get; private set; }
        public float ChooseFlash { get; private set; }
        public Vector2 Position { get; private set; }
        public float Visibility { get; private set; }
        internal bool WasSelected;
        internal bool Opening;
        internal double OpenStartedAt = double.NaN;

        public EntryModeBoard(EntryView.BoardView view, Vector2 savedSlot)
        {
            this.view = view;
            Root = view.root;
            cursor = view.cursor; closed = view.closed; open = view.open; flash = view.flash;
            title = view.title; info = view.info;
            Hit = view.hit;
            Mode = new EntryMode { Title = title.text, Scene = view.scene };
            basePosition = Root.anchoredPosition - new Vector2(savedSlot.x, -savedSlot.y);
        }

        // list_anim_up / list_anim_down: every board moves to its next slot linearly; the
        // more-than-one-slot fade rides the same ramp. The first call places the board.
        public void Retarget(double nowMs, Vector2 slot, float visibility, double slideMs)
        {
            if (double.IsNaN(slideStartedAt))
            {
                from = target = Position = slot;
                fromVisibility = targetVisibility = Visibility = visibility;
                slideStartedAt = nowMs - slideMs;
            }
            if (slot != target || visibility != targetVisibility)
            {
                from = Position; fromVisibility = Visibility;
                target = slot; targetVisibility = visibility;
                slideStartedAt = nowMs;
            }
            float p = (float)Math.Max(0, Math.Min(1, (nowMs - slideStartedAt) / slideMs));
            Position = Vector2.Lerp(from, target, p);
            Visibility = Mathf.Lerp(fromVisibility, targetVisibility, p);
        }

        public void Draw(Vector2 offset, float openness, float fade, float infoAlpha, float cursorAlpha, float titleAlpha, float chooseFlash)
        {
            Root.anchoredPosition = basePosition + new Vector2(offset.x, -offset.y);
            Openness = openness;
            Fade = fade;
            ChooseFlash = chooseFlash;
            bool visible = fade > 0.001f;
            Root.gameObject.SetActive(visible);
            if (!visible) return;
            cursor.Alpha(fade * cursorAlpha);
            closed.Alpha(openness < 0.999f ? fade * (1 - openness) : 0);
            open.Alpha(openness > 0.001f ? fade * openness : 0);
            foreach (var line in info)
            {
                line.alpha = fade * infoAlpha;
                line.enabled = line.alpha > 0.001f;
            }
            title.alpha = titleAlpha;
            title.enabled = titleAlpha > 0.001f;
            title.rectTransform.anchoredPosition = Vector2.Lerp(view.titleClosed.anchoredPosition, view.titleOpen.anchoredPosition, openness);
            flash.Alpha(chooseFlash);
            Hit.rectTransform.sizeDelta = Vector2.Lerp(view.closedHitSize, view.openHitSize, openness);
        }
    }
}
