using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Nijiiro single-player Entry (scenes/entry.cpp + Scripts/entry/*.lua, entry_credit_arcade):
    // the credit screen waits for a drum face hit, 1P joins (nameplate and control guide fade in),
    // and once the join animation would have finished the mode list opens with 演奏ゲーム selected and
    // ゲーム設定 below it; ka moves between the boards and a face hit picks one: 演奏ゲーム goes to
    // SongSelect, ゲーム設定 to GlobalSettingScene. The arcade's 60 s timer is shown as a placeholder that never
    // counts down (user decision: the simulator does not limit the player's time). Not ported: the 3D Don and its join
    // cloud, 2P joining, the other boards (特訓モード / きせかえ), the costume menu and the
    // ALL.Net indicator. The screen is saved in the scene (EntryView); Awake only binds it.
    public sealed class EntryScene : MonoBehaviour
    {
        public const int TimerSeconds = 60;

        public RectTransform stage;
        public EntryView view;

        [Header("Global chrome")]
        public ArcadeOverlayArt overlay;

        [Header("Timelines")]
        public TextAsset backgroundTimeline;
        public TextAsset creditRowTimeline, creditFadeTimeline, modeBoardTimeline, cursorGlowTimeline, modeListTimeline;

        [Header("Audio")]
        public AudioSource bgm;
        public AudioSource sfx, voice;
        public AudioClip don, ka, cloud, entryStart, selectMode;

        public EntryFlow Flow { get; private set; }
        public ArcadeTimerView TimerView { get; private set; }
        public EntryCredit Credit { get; private set; }
        public EntryModeList Board { get; private set; }
        public EntryMode[] Modes { get; private set; }
        public ControlGuideView Guide { get; private set; }
        public CoinOverlayView Coins { get; private set; }
        public NameplateView Nameplate { get; private set; }
        public bool HasLeft { get; private set; }

        SceneSwitcher switcher;
        EntryBackground backdrop;
        CanvasGroup nameplateGroup;
        bool announced, creditGone;
        double modeShownAt = double.NaN;

        public double Now => GameTimeline.FrameTime * 1000;

        static LumenClip Clip(TextAsset asset) => asset != null ? LumenClip.Parse(asset.text) : LumenClip.Empty;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            double now = Now;
            Bind();
            Flow = new EntryFlow(now, Modes.Length);
            switcher.SceneChanging += OnSceneChanging;
            Show(now);
        }

        void Start()
        {
            bgm.SetAudioGroup(AudioGroup.Bgm);
            voice.SetAudioGroup(AudioGroup.Voice);
            sfx.PrepareAudioEffects(don, ka, cloud);
            voice.PrepareAudioTracks(entryStart, selectMode);
            bgm.loop = true;
            bgm.PlayAudio();
        }

        void OnDestroy()
        {
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene)
        {
            bgm.StopAudio(); voice.StopAudio();
        }

        // The boards (box_manager.cpp's order: 演奏ゲーム first, ゲーム設定 last), their texts and the
        // touch areas are saved in the scene; here they only get their clips and callbacks.
        void Bind()
        {
            backdrop = new EntryBackground(view, Clip(backgroundTimeline));
            Board = new EntryModeList(view, Clip(modeBoardTimeline), Clip(cursorGlowTimeline), Clip(modeListTimeline));
            Modes = new EntryMode[Board.Boards.Count];
            for (int i = 0; i < Modes.Length; i++) Modes[i] = Board.Boards[i].Mode;
            Credit = new EntryCredit(view, Clip(creditRowTimeline), Clip(creditFadeTimeline));
            Guide = new ControlGuideView(view.controlGuide, overlay);
            Nameplate = view.nameplate;
            nameplateGroup = view.nameplateGroup;
            if (nameplateGroup != null) nameplateGroup.alpha = 0;
            TimerView = new ArcadeTimerView(overlay, view.timerDigits);
            TimerView.Show(TimerSeconds);
            Coins = new CoinOverlayView(overlay, view.freePlay, view.qrChip, view.inviteBubble, view.invitePlayer, view.inviteMessage);
            // Touch: on the credit screen a tap anywhere joins (the drum's face). On the mode list taps
            // work like SongSelect's boards: tap another board to move to it, tap the open board to
            // pick it; elsewhere a tap does nothing. Vertical swipes move through the boards. The
            // full-stage area sits under the boards so their own tap areas win.
            TouchArea = view.touchArea;
            view.touchRelay.Clicked = TapBackground;
            view.touchSwipe.Swiped = SwipeBoards;
            view.boardSwipe.Swiped = SwipeBoards;
            for (int i = 0; i < view.boards.Length; i++)
            {
                int index = i;
                view.boards[i].hitRelay.Clicked = () => TapBoard(index);
            }
        }

        public Image TouchArea { get; private set; }

        // A tap outside the boards: joins on the credit screen, nothing on the mode list.
        public void TapBackground()
        {
            if (Flow.State == EntryFlow.Phase.SelectSide) Don();
        }

        // A tap on a board: the open (selected) one is picked, another one is moved to.
        public void TapBoard(int index)
        {
            if (Flow.State != EntryFlow.Phase.SelectMode) { Don(); return; }
            if (index == Flow.SelectedMode) Don();
            else Ka(index - Flow.SelectedMode);
        }

        // Swiping the list up brings the board below to the centre, like a right ka.
        public void SwipeBoards(int delta)
        {
            if (Flow.State == EntryFlow.Phase.SelectMode) Ka(delta);
        }

        void Update()
        {
            double now = Now;
            if (!switcher.IsInputBlocked && !HasLeft)
            {
                if (InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm)) Don();
                else if (InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuLeft) || InputManager.GetKeyDown(InputKey.MenuUp)) Ka(-1);
                else if (InputManager.GetKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuRight) || InputManager.GetKeyDown(InputKey.MenuDown)) Ka(1);
            }
            if (!announced && Flow.IsModeReady(now) && !voice.IsAudioPlaying())
            {
                announced = true;
                Play(voice, selectMode);
            }
            if (Flow.IsFinished(now) && !HasLeft)
            {
                HasLeft = true;
                switcher.PracticeMode = view.boards[Flow.SelectedMode].practice;
                switcher.SwitchScene(Modes[Flow.SelectedMode].Scene);
            }
            Show(now);
        }

        // A drum face hit: join on the credit screen, decide on the board.
        public void Don()
        {
            if (switcher.IsInputBlocked || HasLeft) return;
            double now = Now;
            if (Flow.State == EntryFlow.Phase.SelectSide)
            {
                if (!Flow.Join(now)) return;
                Play(sfx, cloud, oneShot: true);
                Play(voice, entryStart);
                Play(sfx, don, oneShot: true);
                return;
            }
            if (!Flow.IsModeReady(now) || Flow.IsSelected) return;
            Play(sfx, don, oneShot: true);
            Flow.Select(now);
        }

        // A rim hit on the mode list: left ka selects the board above, right ka the one below. The
        // ka answers even at either end of the list, as the original's does.
        public void Ka(int delta)
        {
            if (switcher.IsInputBlocked || HasLeft) return;
            double now = Now;
            if (Flow.State != EntryFlow.Phase.SelectMode || !Flow.IsModeReady(now) || Flow.IsSelected) return;
            Play(sfx, ka, oneShot: true);
            Flow.MoveMode(delta, now);
        }

        static void Play(AudioSource source, AudioClip clip, bool oneShot = false)
        {
            if (source == null || clip == null) return;
            if (oneShot) { source.PlayAudioOneShot(clip); return; }
            source.StopAudio();
            source.clip = clip;
            source.PlayAudio();
        }

        void Show(double now)
        {
            backdrop.Show(now - Flow.StartedAt);
            bool modeReady = Flow.IsModeReady(now);
            if (modeReady && double.IsNaN(modeShownAt)) modeShownAt = now;
            Board.Root.gameObject.SetActive(modeReady);
            if (modeReady)
                Board.Show(now, now - modeShownAt, Flow.SelectedMode, Flow.BoardFade(now), Flow.SelectedAt.HasValue ? now - Flow.SelectedAt.Value : -1);

            if (Flow.State == EntryFlow.Phase.SelectSide) Credit.ShowWaiting(now - Flow.StartedAt, now - Flow.StartedAt);
            else if (!creditGone) creditGone = !Credit.ShowDecided(now - Flow.JoinedAt.Value, 0);

            // The guide runs from the scene start over the credit rows, then restarts with the
            // player's own indicator, fading in with the nameplate.
            float plate = Flow.NameplateAlpha(now);
            if (Flow.JoinedAt.HasValue) Guide.Show(now - Flow.JoinedAt.Value, plate);
            else Guide.Show(now - Flow.StartedAt, 1);
            if (nameplateGroup != null) nameplateGroup.alpha = plate;
            // coin_overlay: the 2P invite appears once the credit rows are gone and only 1P is in.
            Coins.ShowInvite(Flow.JoinedAt.HasValue && creditGone, now - Flow.StartedAt);
        }
    }
}
