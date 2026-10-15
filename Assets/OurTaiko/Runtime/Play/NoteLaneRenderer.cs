using System;
using System.Collections.Generic;
using UnityEngine;

namespace OurTaiko
{
    // The note lane of a play: pooled views for the notes, their text (moji) and the bar lines on
    // screen, placed by NoteScroll and culled by LaneWindow. PlayScene owns the scene objects, the
    // session and when to draw; this only draws.
    public sealed class NoteLaneRenderer
    {
        // The scene objects and sprites the lane draws with (PlayScene's serialized fields).
        public sealed class Assets
        {
            public RectTransform NoteLayer, BarLayer, MojiLayer;
            public Sprite[] NoteSprites, AlternateNoteSprites, RollBodySprites, RollTailSprites, MojiSprites;
            public Sprite BalloonTailSprite, MojiRollSprite;
        }

        readonly RectTransform noteLayer, barLayer, mojiLayer;
        readonly Sprite[] noteSprites, alternateNoteSprites, rollBodySprites, rollTailSprites, mojiSprites;
        readonly Sprite balloonTailSprite, mojiRollSprite;
        // A balloon whose counter is up hides itself and its text.
        readonly BalloonCounterView balloonCounter;
        PlaySession Session;
        public double RenderedTime { get; private set; }
        // Views come from pools while a note is on the lane; null when it is not drawn.
        public RectTransform NoteRoot(int index) => shownNotes[index]?.Root;
        public RectTransform MojiRoot(int index) => shownMoji[index]?.Root;
        public RectTransform BarRoot(int index) => shownBars[index];

        NoteView[] shownNotes = new NoteView[0];
        MojiView[] shownMoji = new MojiView[0];
        RectTransform[] shownBars = new RectTransform[0];
        readonly Stack<NoteView>[] notePool = { new Stack<NoteView>(), new Stack<NoteView>(), new Stack<NoteView>() };
        readonly Stack<MojiView>[] mojiPool = { new Stack<MojiView>(), new Stack<MojiView>() };
        readonly Stack<RectTransform> barPool = new Stack<RectTransform>();
        // Candidates for drawing: notes (with their text) and bar lines whose precomputed lane
        // interval holds the render time. Built for the current lane width.
        LaneWindow noteWindow, barWindow;
        float windowWidth = float.NaN;
        readonly List<int> leftLane = new List<int>(), stackOrder = new List<int>();
        // What the last Render drew from; an identical frame (a still pause) is skipped.
        PlaySession renderedSession;
        int renderedVersion, renderedBalloon;
        bool renderedPreview, rendered;
        // Pool keys: the child objects a note or its text needs.
        const int PlainShape = 0, BalloonShape = 1, RollShape = 2;
        sealed class NoteView
        {
            public int Shape;
            public RectTransform Root, Body, Tail;
            public UnityEngine.UI.Image Head, BodyImage, TailImage, BalloonTail;
            public float TailAspect;
        }
        sealed class MojiView
        {
            public int Shape;
            public RectTransform Root, Mid, Tail;
            public UnityEngine.UI.Image Head;
            public float MidAspect;
        }

        public NoteLaneRenderer(Assets assets, BalloonCounterView balloonCounter)
        {
            noteLayer = assets.NoteLayer; barLayer = assets.BarLayer; mojiLayer = assets.MojiLayer;
            noteSprites = assets.NoteSprites; alternateNoteSprites = assets.AlternateNoteSprites;
            rollBodySprites = assets.RollBodySprites; rollTailSprites = assets.RollTailSprites; mojiSprites = assets.MojiSprites;
            balloonTailSprite = assets.BalloonTailSprite; mojiRollSprite = assets.MojiRollSprite;
            this.balloonCounter = balloonCounter;
        }

        static RectTransform Rect(string name, Transform parent, float width, float height)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(width, height); return r;
        }
        static UnityEngine.UI.Image Image(RectTransform r, Sprite sprite)
        {
            var image = r.gameObject.AddComponent<UnityEngine.UI.Image>(); image.sprite = sprite; image.raycastTarget = false; return image;
        }
        static int ShapeOf(ChartNote note) => note.Kind == NoteKind.Balloon ? BalloonShape : note.IsDrumroll ? RollShape : PlainShape;
        static int MojiShapeOf(ChartNote note) => ShapeOf(note) == RollShape ? RollShape - 1 : PlainShape;

        // Only the notes on the lane own views; prewarm enough that the first screens do not allocate.
        public void Prepare(PlaySession session)
        {
            Session = session;
            clocks = new ScrollClockTimeline(session.Chart);
            judgeTravel = JudgeShift.MaxAcross(session.Chart.JudgeMoves);
            shownNotes = new NoteView[Session.Chart.Notes.Count];
            shownMoji = new MojiView[Session.Chart.Notes.Count];
            shownBars = new RectTransform[Session.Chart.Bars.Count];
            for (int i = 0; i < 32; i++) notePool[PlainShape].Push(NewNote(PlainShape));
            for (int i = 0; i < 2; i++) notePool[RollShape].Push(NewNote(RollShape));
            notePool[BalloonShape].Push(NewNote(BalloonShape));
            if (mojiLayer != null)
            {
                for (int i = 0; i < 32; i++) mojiPool[0].Push(NewMoji(0));
                for (int i = 0; i < 2; i++) mojiPool[1].Push(NewMoji(1));
            }
            for (int i = 0; i < 8; i++) barPool.Push(NewBar());
        }
        NoteView NewNote(int shape)
        {
            var root = Rect("Note", noteLayer, NoteSize, NoteSize);
            var view = new NoteView { Shape = shape, Root = root };
            if (shape == RollShape)
            {
                view.Body = Rect("RollBody", root, 0, 0);
                view.Body.anchorMin = new Vector2(0.5f, 0);
                view.Body.anchorMax = new Vector2(0.5f, 1);
                view.Body.pivot = new Vector2(0, 0.5f);
                view.BodyImage = Image(view.Body, null);
                view.Tail = Rect("RollTail", root, 0, 0);
                view.Tail.anchorMin = view.Body.anchorMin;
                view.Tail.anchorMax = view.Body.anchorMax;
                view.Tail.pivot = view.Body.pivot;
                view.TailImage = Image(view.Tail, null);
            }
            var head = Rect("Head", root, 0, 0);
            // Match draw_balloon's balloon_offset as a fraction of the note width.
            // Stretch anchors keep the face aligned when the note or Canvas scales.
            float faceOffset = shape == BalloonShape ? BalloonFace : 0;
            head.anchorMin = new Vector2(-faceOffset, 0);
            head.anchorMax = new Vector2(1 - faceOffset, 1);
            view.Head = Image(head, null);
            if (shape == BalloonShape)
            {
                // notes/10 joins the right edge of notes/7 in draw_balloon.
                var tail = Rect("BalloonTail", root, 0, 0);
                tail.anchorMin = new Vector2(1 - faceOffset, 0);
                tail.anchorMax = new Vector2(2 - faceOffset, 1);
                view.BalloonTail = Image(tail, balloonTailSprite);
            }
            root.gameObject.SetActive(false);
            return view;
        }
        // draw_notes' second pass draws every text after every note, so the text sits in its
        // own layer above the notes; within it earlier notes again paint over later ones.
        MojiView NewMoji(int shape)
        {
            var root = Rect("Moji", mojiLayer, MojiWidth, MojiHeight);
            var view = new MojiView { Shape = shape, Root = root };
            if (shape == 1)
            {
                // draw_drumroll: moji_drumroll_mid from head to tail, then the head and tail text.
                view.Mid = Rect("Mid", root, 0, 0);
                view.Mid.anchorMin = new Vector2(0.5f, 0);
                view.Mid.anchorMax = new Vector2(0.5f, 1);
                view.Mid.pivot = new Vector2(0, 0.5f);
                Image(view.Mid, mojiRollSprite);
                view.MidAspect = mojiRollSprite.rect.width / mojiRollSprite.rect.height;
            }
            var head = Rect("Head", root, 0, 0);
            head.anchorMin = Vector2.zero; head.anchorMax = Vector2.one;
            view.Head = Image(head, null);
            if (view.Mid != null)
            {
                view.Tail = Rect("Tail", root, 0, 0);
                view.Tail.anchorMin = Vector2.zero; view.Tail.anchorMax = Vector2.one;
                Image(view.Tail, mojiSprites[NoteMoji.Tail]);
            }
            root.gameObject.SetActive(false);
            return view;
        }
        RectTransform NewBar()
        {
            var root = Rect("Measure", barLayer, 3, 200);
            Image(root, null);
            root.gameObject.SetActive(false);
            return root;
        }

        NoteView AcquireNote(int index, ChartNote note)
        {
            int shape = ShapeOf(note);
            var view = notePool[shape].Count > 0 ? notePool[shape].Pop() : NewNote(shape);
            view.Root.name = note.Kind.ToString();
            view.Head.sprite = noteSprites[(int)note.Kind];
            if (shape == RollShape)
            {
                int size = note.Kind == NoteKind.BigRoll ? 1 : 0;
                view.BodyImage.sprite = rollBodySprites[size];
                view.TailImage.sprite = rollTailSprites[size];
                view.TailAspect = RollTailAspect(note);
            }
            shownNotes[index] = view;
            view.Root.gameObject.SetActive(true);
            return view;
        }
        void ReleaseNote(int index)
        {
            var view = shownNotes[index];
            view.Root.gameObject.SetActive(false);
            notePool[view.Shape].Push(view);
            shownNotes[index] = null;
        }
        MojiView AcquireMoji(int index, ChartNote note)
        {
            int shape = MojiShapeOf(note);
            var view = mojiPool[shape].Count > 0 ? mojiPool[shape].Pop() : NewMoji(shape);
            view.Root.name = note.Kind + "Moji";
            view.Head.sprite = mojiSprites[note.Moji];
            shownMoji[index] = view;
            view.Root.gameObject.SetActive(true);
            return view;
        }
        void ReleaseMoji(int index)
        {
            var view = shownMoji[index];
            view.Root.gameObject.SetActive(false);
            mojiPool[view.Shape].Push(view);
            shownMoji[index] = null;
        }
        float RollTailAspect(ChartNote note)
        {
            var sprite = rollTailSprites[note.Kind == NoteKind.BigRoll ? 1 : 0];
            return sprite.rect.width / sprite.rect.height;
        }

        // draw_notes walks draw_note_buffer in reverse, so earlier notes paint over
        // later ones; uGUI draws later siblings on top. Pooled views are re-stacked
        // whenever a note enters, since a reused view keeps its old sibling slot.
        // Only candidates hold views, so walking them in descending index order is enough.
        static void Restack<T>(T[] shown, List<int> order, Func<T, Transform> root) where T : class
        {
            for (int k = order.Count - 1; k >= 0; k--)
                if (shown[order[k]] != null) root(shown[order[k]]).SetAsLastSibling();
        }
        void SortCandidates(LaneWindow window)
        {
            stackOrder.Clear();
            for (int k = 0; k < window.Count; k++) stackOrder.Add(window[k]);
            stackOrder.Sort();
        }
        void ReleaseBar(int index)
        {
            var root = shownBars[index];
            root.gameObject.SetActive(false); barPool.Push(root); shownBars[index] = null;
        }

        // Lane intervals depend on the chart and the lane width (the travel distance), so a
        // width change rebuilds them and starts again from an empty lane.
        bool EnsureLaneWindows()
        {
            float width = noteLayer.rect.width;
            if (noteWindow != null && width == windowWidth) return false;
            for (int i = 0; i < shownNotes.Length; i++) { if (shownNotes[i] != null) ReleaseNote(i); if (shownMoji[i] != null) ReleaseMoji(i); }
            for (int i = 0; i < shownBars.Length; i++) if (shownBars[i] != null) ReleaseBar(i);
            windowWidth = width;
            noteWindow = LaneCull.ForNotes(Session.Chart.Notes, clocks, width, JudgeLocalX, Math.Max(NoteSize, MojiWidth), TravelDistance, judgeTravel);
            barWindow = LaneCull.ForBars(Session.Chart.Bars, clocks, width, JudgeLocalX, NoteSize, TravelDistance, judgeTravel);
            return true;
        }

        // Nijiiro: lane x=498/y=276, judge x=618, note top=14 with 192-pixel sprites.
        const float JudgeLocalX = 120, JudgeLocalY = -110;
        // notes/moji frames are 256x48; skin moji.y=209 against notes.y=14, centre to centre.
        const float MojiWidth = 256, MojiHeight = 48, MojiDrop = 209 - 14 + MojiHeight / 2 - NoteSize / 2;
        // Nijiiro note frames are 192x192; draw_balloon shifts the face by 12/128 of the width.
        const float NoteSize = 192, BalloonFace = 12f / 128f;
        // A measure's length on the lane, whatever the lane's width (NoteScroll.MeasureWidth).
        const double TravelDistance = NoteScroll.MeasureWidth;
        // The beat clock of BM/HB-scroll notes (the chart's, the same for every session of it).
        ScrollClockTimeline clocks;

        // How far #JPOSSCROLL may move the judge across (culling margin), and where it is this frame.
        double judgeTravel;
        Vector2 judge;

        Vector2 Position(ChartNote note, double time)
        {
            var (x, y) = NoteScroll.Distance(note, time, clocks, TravelDistance);
            return new Vector2(judge.x + (float)x, judge.y - (float)y);
        }
        // Draws `session` at `time`. `preview` shows the routes a paused practice would play;
        // `hideSkipped` hides notes the practice attempt skipped. Returns false for an unchanged frame.
        public bool Render(PlaySession session, double time, bool preview, bool hideSkipped)
        {
            Session = session;
            // #JPOSSCROLL moves the origin notes are placed from (OpenTaiko GetNoteOriginX/Y).
            var shift = JudgeShift.At(session.Chart.JudgeMoves, time);
            judge = new Vector2(JudgeLocalX + (float)shift.X, JudgeLocalY - (float)shift.Y);
            bool rebuilt = EnsureLaneWindows();
            if (!rebuilt && rendered && time == RenderedTime && Session == renderedSession && Session.Version == renderedVersion
                && balloonCounter.NoteIndex == renderedBalloon && preview == renderedPreview) return false;
            rendered = true; renderedSession = Session; renderedVersion = Session.Version;
            renderedBalloon = balloonCounter.NoteIndex; renderedPreview = preview;
            RenderedTime = time;
            int expression = Session.NoteExpressionFrame(time, preview);
            bool notesEntered = false, mojiEntered = false;
            var chartNotes = Session.Chart.Notes;
            // Outside its interval a note is off the lane, exactly as the full cull would find it.
            leftLane.Clear();
            noteWindow.Seek(time, leftLane);
            foreach (int i in leftLane)
            {
                if (shownNotes[i] != null) ReleaseNote(i);
                if (shownMoji[i] != null) ReleaseMoji(i);
            }
            for (int k = 0; k < noteWindow.Count; k++)
            {
                int i = noteWindow[k];
                var note = chartNotes[i]; var view = shownNotes[i]; var pos = Position(note, time);
                if (note.IsBalloon && time >= note.Time) pos = judge;
                // The tail is placed (and cut to whole pixels) on its own, as OpenTaiko's roll end:
                // `length` across the lane, `rise` up it (a vertical SCROLL turns the roll).
                var tail = note.IsDrumroll ? NoteScroll.TailDistance(note, time, clocks, TravelDistance) : default;
                float length = note.IsDrumroll ? (float)(tail.X + judge.x) - pos.x : 0;
                float rise = note.IsDrumroll ? (float)(judge.y - tail.Y) - pos.y : 0;
                // ドロン hides the notes; they are still judged. Like draw_note_buffer,
                // only a hit removes a note: a 5/6 roll resolved at its tail and a note
                // missed by timeout keep scrolling until they leave the lane.
                bool rolling = note.IsDrumroll;
                // #SUDDEN hides a note and its text until it appears; it is still judged.
                bool alive = note.Display && NoteScroll.Appeared(note, time) && !(hideSkipped && Session.Skipped[i]) && (preview ? Session.IsPracticePreviewActive(note) : Session.IsShown(note)) && (rolling || Session.Missed[i] || !Session.Resolved[i]);
                bool visible = alive && InLane(pos.x, Reach(note, view, length));
                if (mojiLayer != null) mojiEntered |= RenderMoji(i, note, pos, length, alive);
                if (!visible)
                {
                    if (view != null) ReleaseNote(i);
                    continue;
                }
                if (view == null) { view = AcquireNote(i, note); notesEntered = true; }
                else if (!view.Root.gameObject.activeSelf) view.Root.gameObject.SetActive(true);
                view.Head.sprite = expression == 0 ? noteSprites[(int)note.Kind] : alternateNoteSprites[(int)note.Kind];
                view.Root.anchoredPosition = pos;
                if (view.BalloonTail != null) view.BalloonTail.enabled = balloonCounter.NoteIndex != i;
                if (view.Body != null)
                {
                    // draw_drumroll adds the strip's native width to length +
                    // drumroll_width_offset: (48 - 47) / 128 of the note height.
                    // OpenTaiko DisplayRoll turns the body and tail toward the tail; the head stays
                    // upright. Mirroring keeps a leftward roll as before; the turn adds the rise.
                    float overlap = view.Root.rect.height / 128f;
                    float direction = length < 0 ? -1 : 1;
                    float span = Mathf.Sqrt(length * length + rise * rise);
                    var turn = Quaternion.Euler(0, 0, Mathf.Atan2(direction * rise, Mathf.Abs(length)) * Mathf.Rad2Deg);
                    view.Body.sizeDelta = new Vector2(span + overlap, 0);
                    view.Body.localScale = new Vector3(direction, 1, 1);
                    view.Body.localRotation = turn;
                    view.Tail.anchoredPosition = new Vector2(length, rise);
                    view.Tail.sizeDelta = new Vector2(view.Root.rect.height * view.TailAspect, 0);
                    view.Tail.localScale = view.Body.localScale;
                    view.Tail.localRotation = turn;
                }
            }
            if (notesEntered || mojiEntered) SortCandidates(noteWindow);
            if (notesEntered) Restack(shownNotes, stackOrder, v => v.Root);
            if (mojiEntered) Restack(shownMoji, stackOrder, v => v.Root);
            leftLane.Clear();
            barWindow.Seek(time, leftLane);
            foreach (int i in leftLane) if (shownBars[i] != null) ReleaseBar(i);
            for (int k = 0; k < barWindow.Count; k++)
            {
                int i = barWindow[k];
                var bar = Session.Chart.Bars[i];
                var pos = Position(bar, time); pos.y -= 4;
                float half = (bar.IsBranchStart ? 6 : 3) / 2f;
                bool visible = bar.Display && (preview ? Session.IsPracticePreviewActive(bar) : Session.IsShown(bar)) && InLane(pos.x, new Vector2(-half, half));
                var root = shownBars[i];
                if (!visible)
                {
                    if (root != null) ReleaseBar(i);
                    continue;
                }
                if (root == null)
                {
                    root = shownBars[i] = barPool.Count > 0 ? barPool.Pop() : NewBar();
                    root.sizeDelta = new Vector2(bar.IsBranchStart ? 6 : 3, 200);
                    root.GetComponent<UnityEngine.UI.Image>().color = bar.IsBranchStart ? new Color(1, 0.8f, 0.2f, 0.8f) : new Color(1, 1, 1, 0.35f);
                    root.gameObject.SetActive(true);
                }
                root.anchoredPosition = pos;
            }
            return true;
        }

        // The text follows its note's lifetime and is culled by its own extent.
        // Returns whether a pooled view was taken for it this frame.
        bool RenderMoji(int index, ChartNote note, Vector2 pos, float length, bool alive)
        {
            // skip_note: a balloon whose counter is up draws neither the note nor its text.
            alive &= balloonCounter.NoteIndex != index;
            bool roll = MojiShapeOf(note) == 1;
            var view = shownMoji[index];
            // The text follows its head, rolls too (OpenTaiko draws SE notes at the head's y).
            var at = new Vector2(pos.x, pos.y - MojiDrop);
            float half = (view != null ? view.Root.rect.width : MojiWidth) / 2;
            LaneCull.MojiReach(roll, 2 * half, length, out double min, out double max);
            var reach = new Vector2((float)min, (float)max);
            if (!alive || !InLane(at.x, reach))
            {
                if (view != null) ReleaseMoji(index);
                return false;
            }
            bool entered = view == null;
            if (entered) view = AcquireMoji(index, note);
            else if (!view.Root.gameObject.activeSelf) view.Root.gameObject.SetActive(true);
            view.Root.anchoredPosition = at;
            if (!roll) return entered;
            // The strip is drawn native width + length wide from the head, like t_moji_drumroll_mid's x2.
            float width = view.Root.rect.height * view.MidAspect + length;
            view.Mid.sizeDelta = new Vector2(Mathf.Abs(width), 0);
            view.Mid.localScale = new Vector3(width < 0 ? -1 : 1, 1, 1);
            view.Tail.anchoredPosition = new Vector2(length, 0);
            return entered;
        }

        // The lane clip mask is the visible area, in Canvas units that follow the
        // window resolution, so cull against its live rect instead of fixed pixels.
        bool InLane(float x, Vector2 reach) => LaneCull.InLane(x, reach.x, reach.y, noteLayer.rect.width);

        // Horizontal extent of a note's sprites relative to its centre, from the current
        // size of its view, or the design size while it has none.
        Vector2 Reach(ChartNote note, NoteView view, float length)
        {
            Vector2 size = view != null ? view.Root.rect.size : new Vector2(NoteSize, NoteSize);
            float aspect = note.IsDrumroll ? RollTailAspect(note) : 0;
            LaneCull.NoteReach(note, size.x, size.y, length, aspect, BalloonFace, out double min, out double max);
            return new Vector2((float)min, (float)max);
        }
    }
}
