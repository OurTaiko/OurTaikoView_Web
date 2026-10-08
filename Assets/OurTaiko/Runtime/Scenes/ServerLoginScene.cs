using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OurTaiko.Online;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    // Between Entry's play/practice modes and SongSelect, after MajdataPlay's Login scene: each enabled server
    // of servers.json is shown in turn with its account. ログイン logs in and loads the catalog with
    // the account's scores, ゲスト loads the catalog only (no score upload, as in OurTaikoPlayer),
    // スキップ leaves the server out, もどる returns to Entry. A remembered account that logged in
    // last time logs in on its own. With no enabled server the scene goes straight to SongSelect.
    //
    // Keys: ka (or the arrows) moves the focus, don or Enter activates it; Enter in a field moves
    // to the next field or logs in; Esc leaves a field, cancels a running request, or returns.
    public sealed class ServerLoginScene : MonoBehaviour
    {
        public enum Outcome { None, LoggedIn, Guest, Skipped }

        public ServerLoginView view;
        public AudioSource bgm, sfx;
        public AudioClip don, ka;
        [Tooltip("How long a successful connection is shown before the next server.")]
        [Min(0)] public float successSeconds = 0.8f;

        public ServerLoginView.Item Focus { get; private set; } = ServerLoginView.Item.Login;
        public bool IsBusy => request != null;
        public int ServerIndex { get; private set; } = -1;
        public IReadOnlyList<Outcome> Outcomes => outcomes;
        public bool HasLeft { get; private set; }

        SceneSwitcher switcher;
        OnlineManager online;
        readonly List<int> servers = new List<int>();
        readonly List<Outcome> outcomes = new List<Outcome>();
        Task request;
        CancellationTokenSource cancel;
        double advanceAt = -1;
        // TMP_InputField handles Enter/Esc itself; the frame it does so ignores the same key here.
        int fieldKeyFrame = -1;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            online = OnlineManager.EnsureInstance();
            view.Bind(OnClicked);
            view.username.onSubmit.AddListener(_ => OnSubmit(view.username));
            view.password.onSubmit.AddListener(_ => OnSubmit(view.password));
            view.username.onEndEdit.AddListener(_ => fieldKeyFrame = Time.frameCount);
            view.password.onEndEdit.AddListener(_ => fieldKeyFrame = Time.frameCount);
            switcher.SceneChanging += OnSceneChanging;
            // OurTaikoPlayer refreshes every server whenever song select is entered from the mode select.
            online.Disconnect();
            SongSelectManager.EnsureInstance().Reset();
            // Rescan the songs folder meanwhile, so songs added since the last visit appear.
            LocalSongLibrary.EnsureInstance().Refresh();
            for (int i = 0; i < online.Servers.servers.Count; i++)
                if (online.Servers.servers[i].enabled) servers.Add(i);
            if (servers.Count > 0) ShowServer(0);
        }

        void Start()
        {
            bgm.SetAudioGroup(AudioGroup.Bgm);
            sfx.PrepareAudioEffects(don, ka);
            if (servers.Count > 0 && bgm != null && bgm.clip != null) { bgm.loop = true; bgm.PlayAudio(); }
        }

        void OnDestroy()
        {
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
            cancel?.Cancel();
        }

        void OnSceneChanging(string scene)
        {
            if (bgm != null) bgm.StopAudio();
        }

        ServerConfig Server => online.Servers.servers[servers[ServerIndex]];

        void ShowServer(int index)
        {
            ServerIndex = index;
            outcomes.Add(Outcome.None);
            var server = Server;
            view.header.text = "サーバーログイン";
            view.serverName.text = server.DisplayName;
            view.serverUrl.text = server.baseUrl;
            view.progress.text = servers.Count > 1 ? $"{index + 1} / {servers.Count}" : "";
            view.username.text = server.username ?? "";
            view.password.text = server.password ?? "";
            view.ShowMessage(switcher.PracticeMode
                ? "ログインすると過去のスコアを確認できます。練習のスコアは保存しません。"
                : "ログインするとスコアがサーバーに保存されます。ゲストはスコアを保存しません。");
            Focus = server.HasCredentials ? ServerLoginView.Item.Login : ServerLoginView.Item.Username;
            view.ShowFocus(Focus, false);
            if (server.autoLogin && server.HasCredentials) Connect(guest: false);
        }

        void Update()
        {
            if (HasLeft) return;
            if (servers.Count == 0) { if (!switcher.IsSwitching) Leave(SceneSwitcher.SongSelectScene); return; }
            if (advanceAt >= 0)
            {
                if (GameTimeline.FrameTime >= advanceAt) { advanceAt = -1; Next(); }
                return;
            }
            if (switcher.IsInputBlocked) return;
            bool back = InputManager.GetKeyDown(InputKey.Back), confirm = InputManager.GetKeyDown(InputKey.Confirm);
            if (IsBusy)
            {
                var (done, total) = online.Client.CatalogProgress;
                if (total > 0) view.ShowMessage($"曲リストを読み込み中… {done} / {total}");
                if (back) CancelRequest();
                return;
            }
            var editing = Editing;
            if (editing != null)
            {
                // Typed letters also reach the drum keys; while a field is edited only Esc acts here
                // (Enter arrives as the field's onSubmit).
                if (back) { editing.DeactivateInputField(); Deselect(); fieldKeyFrame = Time.frameCount; }
                return;
            }
            if (Time.frameCount == fieldKeyFrame) return;
            if (back) { Play(don); Activate(ServerLoginView.Item.Back); return; }
            if (InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || confirm) Activate(Focus);
            else if (InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuUp) || InputManager.GetKeyDown(InputKey.MenuLeft)) Move(-1);
            else if (InputManager.GetKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuDown) || InputManager.GetKeyDown(InputKey.MenuRight)) Move(1);
        }

        // Enter in the account field moves to the password; Enter in the password logs in.
        void OnSubmit(TMP_InputField field)
        {
            fieldKeyFrame = Time.frameCount;
            if (IsBusy || HasLeft || advanceAt >= 0) return;
            if (field == view.username) { Play(ka); SetFocus(ServerLoginView.Item.Password); EditField(view.password); }
            else Activate(ServerLoginView.Item.Login);
        }

        TMP_InputField Editing => view.username.isFocused ? view.username : view.password.isFocused ? view.password : null;

        public void Move(int delta)
        {
            if (IsBusy || HasLeft) return;
            var order = ServerLoginView.Order;
            int index = Array.IndexOf(order, Focus);
            SetFocus(order[((index + delta) % order.Length + order.Length) % order.Length]);
            Play(ka);
        }

        void SetFocus(ServerLoginView.Item item)
        {
            Focus = item;
            view.ShowFocus(Focus, IsBusy);
        }

        void OnClicked(ServerLoginView.Item item)
        {
            if (HasLeft || switcher.IsInputBlocked || advanceAt >= 0) return;
            if (IsBusy) { if (item == ServerLoginView.Item.Back) CancelRequest(); return; }
            // A selected field is already being edited by TMP_InputField; just follow the focus.
            if (item == ServerLoginView.Item.Username || item == ServerLoginView.Item.Password) { SetFocus(item); return; }
            Activate(item);
        }

        public void Activate(ServerLoginView.Item item)
        {
            if (IsBusy || HasLeft || advanceAt >= 0) return;
            SetFocus(item);
            switch (item)
            {
                case ServerLoginView.Item.Username: Play(don); EditField(view.username); break;
                case ServerLoginView.Item.Password: Play(don); EditField(view.password); break;
                case ServerLoginView.Item.Login:
                    Play(don);
                    if (string.IsNullOrEmpty(view.username.text) || string.IsNullOrEmpty(view.password.text))
                    {
                        view.ShowMessage("ユーザー名とパスワードを入力してください。", error: true);
                        break;
                    }
                    Connect(guest: false);
                    break;
                case ServerLoginView.Item.Guest: Play(don); Connect(guest: true); break;
                case ServerLoginView.Item.Skip:
                    Play(don);
                    outcomes[ServerIndex] = Outcome.Skipped;
                    Next();
                    break;
                case ServerLoginView.Item.Back: Play(don); Leave(SceneSwitcher.EntryScene); break;
            }
        }

        void EditField(TMP_InputField field)
        {
            field.Select();
            field.ActivateInputField();
            field.MoveTextEnd(false);
        }

        static void Deselect() => UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);

        async void Connect(bool guest)
        {
            Deselect();
            var config = Server.Clone();
            if (!guest) { config.username = view.username.text; config.password = view.password.text; }
            int index = servers[ServerIndex];
            cancel = new CancellationTokenSource();
            online.Client.ResetCatalogProgress();
            var endpoint = online.Client.Add(config);
            view.ShowMessage(guest ? "サーバーに接続中…" : "ログイン中…");
            request = online.Client.ConnectAsync(endpoint, guest, cancel.Token);
            view.ShowFocus(Focus, true);
            try
            {
                await request;
                if (this == null) return;
                if (guest) online.Remember(index, Server.username, Server.password, autoLogin: false);
                else online.Remember(index, config.username, config.password, autoLogin: true);
                outcomes[ServerIndex] = guest ? Outcome.Guest : Outcome.LoggedIn;
                string who = guest ? "ゲスト" : endpoint.Nickname;
                view.ShowMessage($"{who}として接続しました（{endpoint.ChartCount} 曲）", success: true);
                advanceAt = GameTimeline.FrameTime + successSeconds;
            }
            catch (Exception error)
            {
                if (this == null) return;
                Debug.LogWarning($"ServerLogin: {Server.DisplayName}: {error.Message}");
                if (!guest && error is HttpStatusException status && (status.Status == 401 || status.Status == 403))
                    online.Remember(index, Server.username, Server.password, autoLogin: false);
                view.ShowMessage(Describe(error, cancel.IsCancellationRequested), error: true);
            }
            finally
            {
                request = null;
                cancel?.Dispose(); cancel = null;
                if (this != null) view.ShowFocus(Focus, false);
            }
        }

        void CancelRequest()
        {
            Play(don);
            cancel?.Cancel();
        }

        // fanmade.cpp's codes, worded for the player (the code stays visible for reports).
        public static string Describe(Exception error, bool cancelled)
        {
            if (cancelled) return "キャンセルしました。";
            if (error is HttpStatusException status)
                switch (status.Status)
                {
                    case 401: case 403: return "ユーザー名またはパスワードが正しくありません。";
                    case 429: return "ログインの試行回数が多すぎます。しばらくしてから再試行してください。";
                }
            return "接続できませんでした：" + error.Message;
        }

        void Next()
        {
            if (ServerIndex + 1 < servers.Count) { ShowServer(ServerIndex + 1); return; }
            Leave(SceneSwitcher.SongSelectScene);
        }

        void Leave(string scene)
        {
            if (HasLeft) return;
            HasLeft = true;
            if (scene == SceneSwitcher.SongSelectScene) online.RefreshSongs();
            switcher.SwitchScene(scene);
        }

        void Play(AudioClip clip)
        {
            if (sfx != null && clip != null) sfx.PlayAudioOneShot(clip);
        }
    }
}
