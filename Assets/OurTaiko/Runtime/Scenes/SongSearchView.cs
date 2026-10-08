using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace OurTaiko
{
    // Saved uGUI dialog; the manager owns results so a played song returns to the same search.
    public sealed class SongSearchView : MonoBehaviour, IPointerClickHandler
    {
        public GameObject panel;
        public TMP_InputField keyword;
        public UnityEngine.UI.Button apply, clear, close;
        public UnityEngine.UI.Button[] previous, next;
        public TextMeshProUGUI[] values, labels;
        public TextMeshProUGUI title, status, summary, hint;
        public RectTransform cursor;
        SongSelectManager manager;
        CancellationTokenSource request;
        int difficulty, level, order, row, closedFrame = -1, fieldSubmitFrame = -1, openedFrame = -1, editingFrame = -1, compositionFrame = -1;
        bool busy, editingKeyword;
        string composition = "";
        Keyboard keyboard;
        void OnEnable() { keyboard = Keyboard.current; if (keyboard != null) keyboard.onIMECompositionChange += OnComposition; }
        void OnComposition(UnityEngine.InputSystem.LowLevel.IMECompositionString value) { composition = value.ToString(); compositionFrame = Time.frameCount; }
        public bool IsOpen => panel.activeSelf;
        public bool EditingKeyword => editingKeyword;
        public int FocusedRow => row;
        public bool BlocksInput => IsOpen || Time.frameCount == closedFrame;
        string Language => SettingManager.Instance?.Settings.general.Language ?? "en";
        string Words(string en, string ja, string zh) => Language == "ja" ? ja : Language.StartsWith("zh") ? zh : en;

        public void Bind(SongSelectManager model)
        {
            manager = model;
            apply.onClick.AddListener(Submit);
            clear.onClick.AddListener(Clear);
            close.onClick.AddListener(Close);
            for (int i = 0; i < previous.Length; i++)
            {
                int index = i;
                previous[i].onClick.AddListener(() => Change(index, -1));
                next[i].onClick.AddListener(() => Change(index, 1));
            }
            keyword.onSubmit.AddListener(_ => FinishKeyword());
            keyword.onValueChanged.AddListener(_ => { if (busy) { Cancel(); status.text = ""; Draw(); } });
            keyword.shouldActivateOnSelect = false;
            keyword.enabled = false;
            panel.SetActive(false);
            RefreshSummary();
        }
        void OnDisable() { editingKeyword = false; if (keyboard != null) keyboard.onIMECompositionChange -= OnComposition; Cancel(); if (manager != null) manager.SearchDialogOpen = false; }
        void Cancel() { manager?.CancelSearch(); request?.Cancel(); request?.Dispose(); request = null; busy = false; }
        public void Open()
        {
            if (manager == null || manager.Phase != SongSelectManager.State.Browsing || SceneSwitcher.EnsureInstance().IsInputBlocked) return;
            var query = manager.SearchQuery;
            difficulty = query.Difficulty.HasValue ? (int)query.Difficulty.Value + 1 : 0;
            level = query.Level; order = (int)query.Order; row = 0;
            editingKeyword = false; composition = ""; keyword.enabled = false;
            EventSystem.current?.SetSelectedGameObject(null);
            keyword.SetTextWithoutNotify(query.Keyword);
            openedFrame = Time.frameCount;
            manager.SearchDialogOpen = true;
            panel.SetActive(true);
            status.text = "";
            Draw();
        }
        public void Close() => CloseDialog(true);
        void CloseDialog(bool playSound)
        {
            if (editingKeyword || Time.frameCount == fieldSubmitFrame) return;
            if (playSound) manager.PlaySearchSound(SongSelectManager.Sound.Don);
            Cancel(); keyword.DeactivateInputField();
            EventSystem.current?.SetSelectedGameObject(null);
            panel.SetActive(false); manager.SearchDialogOpen = false; closedFrame = Time.frameCount;
            RefreshSummary();
        }
        public void Clear()
        {
            if (editingKeyword || Time.frameCount == fieldSubmitFrame) return;
            manager.PlaySearchSound(SongSelectManager.Sound.Don);
            Cancel(); manager.ClearSearch(); CloseDialog(false);
        }
        // The disabled TMP field lets its pointer clicks bubble to this saved parent view.
        // Selecting a row and activating text entry are deliberately separate gestures.
        public void OnPointerClick(PointerEventData eventData)
        {
            var hit = eventData.pointerPressRaycast.gameObject;
            if (!IsOpen || editingKeyword || Time.frameCount == fieldSubmitFrame || eventData.button != PointerEventData.InputButton.Left
                || hit == null || !hit.transform.IsChildOf(keyword.transform)) return;
            if (row == 3) BeginKeyword();
            else { MoveRow(3 - row, SongSelectManager.Sound.Ka); Draw(); }
        }
        void BeginKeyword()
        {
            manager.PlaySearchSound(SongSelectManager.Sound.Ka);
            Cancel(); status.text = ""; editingKeyword = true; editingFrame = Time.frameCount;
            keyword.enabled = true;
            keyword.Select(); keyword.ActivateInputField(); Draw();
        }
        void FinishKeyword()
        {
            if (!editingKeyword || Time.frameCount == editingFrame || !string.IsNullOrEmpty(composition) || Time.frameCount == compositionFrame) return;
            fieldSubmitFrame = Time.frameCount;
            editingKeyword = false;
            keyword.DeactivateInputField(); keyword.enabled = false;
            EventSystem.current?.SetSelectedGameObject(null);
            Draw();
        }
        void Change(int index, int delta)
        {
            if (editingKeyword || Time.frameCount == fieldSubmitFrame) return;
            manager.PlaySearchSound(SongSelectManager.Sound.Ka);
            Cancel(); row = index;
            keyword.DeactivateInputField(); EventSystem.current?.SetSelectedGameObject(null);
            if (index == 0) difficulty = (difficulty + delta + 6) % 6;
            if (index == 1) level = (level + delta + 11) % 11;
            if (index == 2) order = (order + delta + 3) % 3;
            status.text = ""; Draw();
        }
        public async void Submit()
        {
            if (!IsOpen || busy || editingKeyword || Time.frameCount == fieldSubmitFrame || Time.frameCount == openedFrame) return;
            SongSearchQuery query;
            try { query = new SongSearchQuery(keyword.text, difficulty == 0 ? (Difficulty?)null : (Difficulty)(difficulty - 1), level, (SongSearchOrder)order); }
            catch (ArgumentException) { status.text = Words("Keyword is too long (200 bytes).", "Keyword が長すぎます（200バイト）。", "Keyword 过长（最多 200 字节）。"); return; }
            manager.PlaySearchSound(SongSelectManager.Sound.Don);
            Cancel(); request = new CancellationTokenSource(); var current = request;
            busy = true; Draw(); status.text = Words("Searching…", "検索中…", "搜索中…");
            try
            {
                await manager.SearchAsync(query, current.Token);
                if (request != current || !IsOpen) return;
                CloseDialog(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (request == current) status.text = Words("Search failed: ", "検索失敗: ", "搜索失败：") + error.Message; }
            finally { if (request == current) { busy = false; request.Dispose(); request = null; Draw(); } }
        }
        void MoveRow(int delta, SongSelectManager.Sound sound)
        {
            row = (row + delta + 7) % 7;
            manager.PlaySearchSound(sound);
        }
        public void Tick()
        {
            summary.gameObject.SetActive(manager.Phase == SongSelectManager.State.Browsing && !IsOpen);
            if (SceneSwitcher.EnsureInstance().IsInputBlocked) return;
            if (!IsOpen || Time.frameCount == openedFrame) return;
            if (Time.frameCount == fieldSubmitFrame) return;
            // Only confirmation releases text entry. TMP owns typing, arrows and IME;
            // escape, Tab, drum bindings and outside clicks never operate the menu here.
            if (editingKeyword)
            {
                if (InputManager.GetKeyDown(InputKey.Confirm)) FinishKeyword();
                if (editingKeyword && !keyword.isFocused) { keyword.Select(); keyword.ActivateInputField(); }
                return;
            }
            if (InputManager.GetKeyDown(InputKey.Back)) { Close(); return; }
            if (Keyboard.current?.tabKey.wasPressedThisFrame == true)
            {
                keyword.DeactivateInputField(); EventSystem.current?.SetSelectedGameObject(null);
                MoveRow(Keyboard.current.shiftKey.isPressed ? -1 : 1, SongSelectManager.Sound.Ka); Draw(); return;
            }
            if (InputManager.GetKeyDown(InputKey.MenuUp)) MoveRow(-1, SongSelectManager.Sound.Ka);
            else if (InputManager.GetKeyDown(InputKey.MenuDown)) MoveRow(1, SongSelectManager.Sound.Ka);
            else if (row == 3 && (InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.RightKa))) BeginKeyword();
            else if (InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuLeft))
            { if (row < 3) Change(row, -1); else MoveRow(-1, SongSelectManager.Sound.Ka); }
            else if (InputManager.GetKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuRight))
            { if (row < 3) Change(row, 1); else MoveRow(1, SongSelectManager.Sound.Ka); }
            else if (InputManager.GetKeyDown(InputKey.Confirm) || InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon))
            {
                if (row < 4) MoveRow(1, SongSelectManager.Sound.Don);
                else if (row == 4) Submit(); else if (row == 5) Clear(); else Close();
            }
            Draw();
        }
        void Draw()
        {
            title.text = Words("Song search", "曲をさがす", "歌曲搜索");
            string all = Words("All", "すべて", "全部");
            labels[0].text = Words("Difficulty", "むずかしさ", "难度"); labels[1].text = Words("Stars", "★の数", "星级");
            labels[2].text = Words("Sort by", "表示順", "排序"); labels[3].text = "Keyword";
            values[0].text = difficulty == 0 ? all : new[] { "Easy", "Normal", "Hard", "Oni", "Ura" }[difficulty - 1];
            values[1].text = level == 0 ? all : "★ " + level;
            values[2].text = order == 0 ? Words("Default", "いつもどおり", "通常") : order == 1 ? Words("Not FC first", "未フルコンボ優先", "未 FC 优先") : Words("Not AP first", "未ドンダフル優先", "未 AP 优先");
            apply.GetComponentInChildren<TMP_Text>().text = busy ? Words("Searching…", "検索中…", "搜索中…") : Words("Search", "検索", "搜索");
            clear.GetComponentInChildren<TMP_Text>().text = Words("Clear filters", "解除", "清除筛选");
            close.GetComponentInChildren<TMP_Text>().text = Words("Back", "もどる", "返回");
            hint.text = editingKeyword
                ? Words("Enter / keyboard Done: finish typing", "Enter／キーボードの完了：入力を終了", "回车／键盘完成：结束输入")
                : Words("Don / Tab / ↑↓ Select · Ka Change / type Keyword · Enter Confirm", "ドン / Tab / ↑↓ 選択 · カッ 変更／Keyword入力 · Enter 決定", "咚 / Tab / ↑↓ 选择 · 咔 调整／输入 Keyword · 回车 确认");
            cursor.gameObject.SetActive(row < 4);
            if (row < 4) cursor.Center(960, new[] { 400, 541, 682, 813 }[row]);
            apply.interactable = !busy && !editingKeyword;
            clear.interactable = close.interactable = !editingKeyword;
            foreach (var button in previous) button.interactable = !editingKeyword;
            foreach (var button in next) button.interactable = !editingKeyword;
            foreach (var pair in new[] { (apply, 4), (clear, 5), (close, 6) })
                pair.Item1.targetGraphic.color = row == pair.Item2 ? new Color(1, .85f, .35f) : Color.white;
        }
        public void RefreshSummary()
        {
            summary.text = manager.SearchActive
                ? Words("Results: ", "検索結果: ", "搜索结果：") + manager.SearchCount + (manager.SearchQuery.Keyword.Length > 0 ? "  ·  " + manager.SearchQuery.Keyword : "")
                  + (manager.SearchStatus.Length > 0 ? "\n" + manager.SearchStatus : "")
                : "";
        }
    }
}
