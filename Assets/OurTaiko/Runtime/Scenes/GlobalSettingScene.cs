using UnityEngine;

namespace OurTaiko
{
    // GlobalSettingScene, reached from Entry's ゲーム設定 board. Drum keys drive SettingsMenu: ka
    // (left = up, right = down) moves within the focused list, don confirms; Esc steps back one
    // level. Taps work like SongSelect's boards (tap another row to move to it, tap the focused row
    // to confirm); the choice popup takes a tap on a choice to apply it and a tap outside it to
    // close it, and both lists also take vertical swipes. Each
    // applied choice is saved at once through SettingManager; leaving returns to Entry.
    public sealed class GlobalSettingScene : MonoBehaviour
    {
        public GlobalSettingView view;
        public AudioSource bgm, sfx;
        public AudioClip don, ka, previewVoice;

        public SettingsMenu Menu { get; private set; }
        public bool HasLeft { get; private set; }

        SceneSwitcher switcher;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            var settings = SettingManager.EnsureInstance().Settings;
            Menu = new SettingsMenu(SettingsMenu.Catalog(), settings);
            view.Bind(i => Run(() => Menu.TapType(i)), i => Run(() => Menu.TapItem(i)), i => Run(() => Menu.TapChoice(i)),
                d => Run(() => Menu.SwipeTypes(d)), d => Run(() => Menu.SwipeItems(d)),
                () => { if (Menu.Focus == SettingsFocus.Choice) Run(() => Menu.Back()); },
                d => { if (Menu.Focus == SettingsFocus.Choice) Ka(d); });
            view.Show(Menu);
            switcher.SceneChanging += OnSceneChanging;
        }

        void Start()
        {
            bgm.SetAudioGroup(AudioGroup.Bgm);
            if (bgm == null || bgm.clip == null) return;
            sfx.PrepareAudioEffects(don, ka);
            bgm.loop = true;
            bgm.PlayAudio();
        }

        void OnDestroy()
        {
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene)
        {
            if (bgm != null) bgm.StopAudio();
        }

        void Update()
        {
            if (switcher.IsInputBlocked || HasLeft) return;
            if (InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm))
                Don();
            else if (InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuUp) || InputManager.GetKeyDown(InputKey.MenuLeft))
                Ka(-1);
            else if (InputManager.GetKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuDown) || InputManager.GetKeyDown(InputKey.MenuRight))
                Ka(1);
            else if (InputManager.GetKeyDown(InputKey.Back))
                Run(() => Menu.Back());
        }

        public void Don() => Run(() => Menu.Don());
        public void Ka(int delta) => Run(() => Menu.Ka(delta));

        void Run(System.Func<SettingsMenu.Result> action)
        {
            if (switcher.IsInputBlocked || HasLeft) return;
            Handle(action());
        }
        void Handle(SettingsMenu.Result result)
        {
            if (result == SettingsMenu.Result.None || switcher.IsInputBlocked || HasLeft) return;
            // Moves answer with the rim sound, everything that confirms or steps with the face.
            if (result == SettingsMenu.Result.Changed)
            {
                SettingManager.EnsureInstance().Set(Menu.Settings);
                if (Menu.CurrentItem.Label == "Drum Volume") sfx.PlayAudioOneShot(don, AudioGroup.Drum);
                else if (Menu.CurrentItem.Label == "Voice Volume" && previewVoice != null) sfx.PlayAudioOneShot(previewVoice, AudioGroup.Voice);
                else Play(don);
            }
            else Play(result == SettingsMenu.Result.Moved ? ka : don);
            if (result == SettingsMenu.Result.Exit)
            {
                HasLeft = true;
                SaveAndLeave();
            }
            view.Show(Menu);
        }

        async void SaveAndLeave()
        {
            try
            {
                SettingManager.EnsureInstance().Set(Menu.Settings);
                await switcher.SwitchSceneAfterFadeAsync(SceneSwitcher.EntryScene,
                    () => AudioEngine.EnsureInstance().ApplyPendingSettingsAsync());
            }
            catch (System.Exception error)
            {
                if (this == null) return;
                HasLeft = false;
                Menu.Settings.audio = SettingManager.EnsureInstance().Settings.Clone().audio;
                view.Show(Menu);
                if (view.outputStatus != null) view.outputStatus.text = error.Message;
                Debug.LogWarning("[Audio] " + error.Message);
                if (bgm != null && bgm.clip != null) bgm.PlayAudio();
            }
        }

        void Play(AudioClip clip)
        {
            if (sfx != null && clip != null) sfx.PlayAudioOneShot(clip);
        }
    }
}
