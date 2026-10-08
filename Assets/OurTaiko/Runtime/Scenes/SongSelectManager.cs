using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OurTaiko
{
    // The song select's state and rules (the view model), after MajdataPlay's split of SongStorage
    // (collections and cursor, kept across scenes) and ListManager (input and flow) from the
    // CoverListManager/displayers that draw. It owns the wheel items, the focus, the phase, the open
    // folder, the course cursor and the 演奏オプション menu, and every command the drum, keys and
    // touch reach. SongSelectScene draws it: it follows the events below with its own animations,
    // sounds and preview, and reports back only what the screen decides (CourseReady, OptionsHidden).
    // A global singleton like OnlineManager; Load rebuilds the wheel each time song select opens.
    public sealed partial class SongSelectManager : MonoBehaviour
    {
        public enum State { Browsing, CourseSelect, Decided }
        public enum ItemKind { Song, Folder, Back, Search }
        public enum Sound { Don, Ka, UraSwitch }
        public enum Voice { StartSong, Options }
        public enum FocusReason { Navigate, FolderOpened, FolderClosed }

        // One board on the wheel. Folder is the folders[] index of a folder or もどる board
        // (-1: the root もどる), or of the folder a song was opened from.
        public sealed class WheelItem
        {
            public ItemKind Kind;
            public SongDefinition Song;
            public SongInfo Info;
            public int Folder = -1;
            // A song wears its folder's genre (the box.def or category it came from), else its own.
            public int Genre;
        }

        // Boards a folder open/close took off the wheel and put on it; Added start on Anchor's slot.
        public sealed class FolderChange
        {
            public WheelItem Anchor;
            public readonly List<WheelItem> Removed = new List<WheelItem>(), Added = new List<WheelItem>();
        }

        const int BackEvery = 10;
        public const string BackLabel = "もどる";

        public static SongSelectManager Instance { get; private set; }

        // The local songs (LocalSongLibrary) ahead of the folders: local box.def folders, then server categories.
        public SongDefinition[] Songs { get; private set; } = Array.Empty<SongDefinition>();
        public IReadOnlyList<SongFolder> Folders => folders;
        public IReadOnlyList<WheelItem> Items => items;
        public int Focused { get; private set; }
        public WheelItem FocusedItem => items.Count > 0 ? items[Focused] : null;
        // null while a folder or もどる board is focused.
        public SongDefinition FocusedSong => FocusedItem?.Song;
        public ItemKind FocusedKind => items[Focused].Kind;
        public int BoardCount => items.Count;
        public ItemKind KindAt(int index) => items[index].Kind;
        public SongDefinition SongAt(int index) => items[index].Song;
        // The open folder's key, or null.
        public string OpenFolder => openFolder >= 0 ? folders[openFolder].Key : null;
        // The folder song select had open, reopened when it comes back from a song; ServerLogin
        // closes every folder again (Reset).
        public string OpenFolderKey { get; private set; }
        public State Phase { get; private set; } = State.Browsing;
        public DifficultyCursor Cursor { get; private set; }
        // The open 演奏オプション menu; it stays set while the panel slides out (IsOptionsClosing).
        public OptionMenu OptionMenu { get; private set; }
        public bool IsOptionPanelOpen => OptionMenu != null;
        public bool IsOptionsClosing => OptionMenu != null && OptionMenu.IsConfirmed;
        public bool AutoPlay => PlayOptions.Shared.auto;
        // The view: whether the course panel has faded in (it ignores input until then).
        public Func<bool> CourseReady { get; set; }

        public event Action<Sound> SoundRequested;
        public event Action<Voice> VoiceRequested;
        public event Action<FolderChange> FolderChanged;
        public event Action<WheelItem, FocusReason> FocusChanged;   // the previously focused item
        public event Action<State> PhaseChanged;                      // the previous phase
        public event Action UraToggled;
        public event Action<OptionMenu> OptionsOpened;
        public event Action<int> OptionChanged;                       // the arrow direction
        public event Action OptionsClosing;

        readonly List<WheelItem> items = new List<WheelItem>();
        SongFolder[] folders = Array.Empty<SongFolder>();
        // The open folder, its もどる board's index and how many boards follow it.
        int openFolder = -1, openAt = -1, openCount;
        int hitSoundCount;

        SceneSwitcher Switcher => SceneSwitcher.EnsureInstance();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() { } // Embedded player does not start desktop catalog services.

        public static SongSelectManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<SongSelectManager>();
            if (existing != null) { existing.Initialize(); return Instance; }
            new GameObject(nameof(SongSelectManager)).AddComponent<SongSelectManager>();
            return Instance;
        }

        void Awake() => Initialize();

        void Initialize()
        {
            if (Instance == this) return;
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ServerLogin: the next song select starts with every folder closed.
        public void Reset() { OpenFolderKey = null; ClearSearchState(); }

        // ---------------------------------------------------------------- loading

        // Rebuilds the wheel for a new song select: local songs, the closed folders, the root もどる;
        // coming back from a song in a folder reopens that folder on the song (reopen_folder_path).
        public void Load(int hitSounds)
        {
            hitSoundCount = hitSounds;
            Phase = State.Browsing;
            Cursor = null;
            OptionMenu = null;
            CourseReady = null;
            var library = LocalSongLibrary.EnsureInstance();
            var online = Online.OnlineManager.EnsureInstance();
            Songs = library.Songs.ToArray();
            folders = library.Folders.Concat(online.Folders).ToArray();
            items.Clear();
            openFolder = -1; openAt = -1; openCount = 0;
            foreach (var song in Songs) items.Add(SongItem(song, -1));
            for (int f = 0; f < folders.Length; f++) items.Add(new WheelItem { Kind = ItemKind.Folder, Folder = f, Genre = folders[f].Genre });
            items.Add(NewSearchItem());
            // The root もどる, which returns to Entry.
            items.Add(new WheelItem { Kind = ItemKind.Back });

            var selected = Switcher.SelectedSong;
            int remembered = Array.IndexOf(Songs, selected);
            int reopen = Array.FindIndex(folders, f => f.Key == OpenFolderKey);
            if (reopen >= 0)
            {
                InsertFolder(FolderItemIndex(reopen), null);
                int inside = items.FindIndex(openAt, openCount + 1, item => item.Song != null && item.Song == selected);
                remembered = inside >= 0 ? inside : openAt;
            }
            Focused = remembered >= 0 ? remembered : 0;
            if (SearchActive) BuildSearchWheel(selected);
        }

        WheelItem SongItem(SongDefinition song, int folder) => new WheelItem
        {
            Kind = ItemKind.Song, Song = song, Info = song.ReadDisplayInfo(), Folder = folder,
            Genre = folder >= 0 ? folders[folder].Genre : song.genre,
        };

        // ---------------------------------------------------------------- input

        // Reads this frame's keys (the view calls it first in its Update).
        public void HandleInput()
        {
            if (Switcher.IsInputBlocked || SearchDialogOpen) return;
            // song_select.cpp: the back key leaves for Entry from any state (an open option panel closes first).
            if (InputManager.GetKeyDown(InputKey.Back)) { Back(); return; }
            bool leftKa = InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuLeft);
            bool rightKa = InputManager.GetKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuRight);
            bool donHit = InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm);
            if (leftKa) Left();
            else if (rightKa) Right();
            else if (donHit) Confirm();
        }

        public bool AcceptsInput()
        {
            if (Switcher.IsInputBlocked || SearchDialogOpen || Phase == State.Decided) return false;
            // The course panel ignores input while it is still fading in.
            return Phase != State.CourseSelect || CourseReady == null || CourseReady();
        }

        public void Back()
        {
            if (SearchDialogOpen) return;
            if (Phase == State.Browsing && SearchActive) { ClearSearch(); return; }
            if (IsOptionPanelOpen) CloseOptions();
            else if (Phase == State.Browsing && openFolder >= 0) { if (AcceptsInput()) { Play(Sound.Don); CloseFolder(); } }
            else Switcher.SwitchScene(SceneSwitcher.EntryScene);
        }

        public void Left()
        {
            if (!AcceptsInput()) return;
            Play(Sound.Ka);
            if (IsOptionPanelOpen) ChangeOption(-1);
            else if (Phase == State.Browsing) Navigate(-1);
            else Cursor.Left();
        }

        public void Right()
        {
            if (!AcceptsInput()) return;
            Play(Sound.Ka);
            if (IsOptionPanelOpen) ChangeOption(+1);
            else if (Phase == State.Browsing) Navigate(+1);
            else if (Cursor.Right()) ToggledUra();
        }

        public void Confirm()
        {
            if (!AcceptsInput()) return;
            Play(Sound.Don);
            if (IsOptionPanelOpen) { OptionMenu.Confirm(); return; }
            if (Phase == State.Browsing)
            {
                var focused = items[Focused];
                if (focused.Kind == ItemKind.Search) SearchRequested?.Invoke();
                else if (focused.Kind == ItemKind.Folder) OpenFolderAt(Focused);
                else if (focused.Kind == ItemKind.Back && SearchActive) ClearSearch();
                else if (focused.Kind == ItemKind.Back && focused.Folder < 0) Switcher.SwitchScene(SceneSwitcher.EntryScene);
                else if (focused.Kind == ItemKind.Back) CloseFolder();
                else EnterCourseSelect();
                return;
            }
            switch (Cursor.Selected)
            {
                case Difficulty.Back: ExitCourseSelect(); break;
                case Difficulty.Modifier: OpenOptions(); break;
                default:
                    SetPhase(State.Decided);
                    Switcher.LastDifficulty = (int)Cursor.Selected;
                    VoiceRequested?.Invoke(Voice.StartSong);
                    break;
            }
        }

        // The view calls this once the start voice has finished.
        public void StartSong()
        {
            if (Phase != State.Decided) return;
            var course = items[Focused].Info.Course(Cursor.Selected);
            Switcher.Play(FocusedSong, course.Course, AutoPlay);
        }

        // Leaving the scene keeps option changes made with the panel still open.
        public void Leave()
        {
            if (IsOptionPanelOpen) PlayOptions.Shared.Save();
        }

        void Play(Sound sound) => SoundRequested?.Invoke(sound);

        void ToggledUra()
        {
            // Both drum input and long presses use the same 90-frame card flip.
            Play(Sound.UraSwitch);
            UraToggled?.Invoke();
        }

        void SetPhase(State phase)
        {
            var previous = Phase;
            Phase = phase;
            PhaseChanged?.Invoke(previous);
        }

        // ---------------------------------------------------------------- touch

        // A tap on a board: another board moves the wheel there, the focused one confirms.
        public void SelectItem(WheelItem item)
        {
            if (Phase != State.Browsing || !AcceptsInput()) return;
            int index = items.IndexOf(item);
            if (index < 0) return;
            if (index == Focused) { Confirm(); return; }
            Play(Sound.Ka);
            int count = items.Count, delta = index - Focused;
            if (delta > count / 2) delta -= count;
            else if (delta < -count / 2) delta += count;
            Navigate(delta);
        }

        // A tap on a course card or the back/option button walks the cursor by the drum's rules.
        public void SelectCourse(Difficulty difficulty)
        {
            if (!AcceptsInput() || Phase != State.CourseSelect || IsOptionPanelOpen) return;
            var target = difficulty == Difficulty.Oni && Cursor.IsUra ? Difficulty.Ura : difficulty;
            if (target >= Difficulty.Easy && items[Focused].Info.Course(target) == null) return;
            if (Cursor.Selected == target) { Confirm(); return; }
            for (int guard = 0; guard < 8 && Cursor.Selected != target; guard++)
            {
                if (Order(Cursor.Selected) < Order(target)) Cursor.Right(); else Cursor.Left();
            }
            if (Cursor.Selected == target)
            {
                if (target < Difficulty.Easy) Confirm();
                else Play(Sound.Ka);
            }
            static int Order(Difficulty d) => d == Difficulty.Ura ? (int)Difficulty.Oni : (int)d;
        }

        public bool CanLongPressOni() => AcceptsInput() && Phase == State.CourseSelect && !IsOptionPanelOpen
            && items[Focused].Info.Has(Difficulty.Oni) && items[Focused].Info.Has(Difficulty.Ura);

        public void LongPressOni()
        {
            if (CanLongPressOni() && Cursor.TryToggleUra()) ToggledUra();
        }

        // ---------------------------------------------------------------- wheel

        public void Navigate(int delta)
        {
            if (items.Count == 0) return;
            var previous = items[Focused];
            int count = items.Count;
            Focused = ((Focused + delta) % count + count) % count;
            FocusChanged?.Invoke(previous, FocusReason.Navigate);
        }

        // ---------------------------------------------------------------- folders

        int FolderItemIndex(int folder) => items.FindIndex(item => item.Kind == ItemKind.Folder && item.Folder == folder);

        // Opening a folder closes the open one first (collapse_inline_now), then opens inline.
        public void OpenFolderAt(int index)
        {
            var previous = items[Focused];
            int folder = items[index].Folder;
            if (openFolder >= 0) { CollapseFolder(); index = FolderItemIndex(folder); }
            InsertFolder(index, FolderChanged);
            Focused = openAt;
            OpenFolderKey = folders[folder].Key;
            FocusChanged?.Invoke(previous, FocusReason.FolderOpened);
        }

        // もどる (or Back) closes the folder and focuses its board again.
        public void CloseFolder()
        {
            if (openFolder < 0) return;
            var previous = items[Focused];
            int folder = openFolder;
            CollapseFolder();
            Focused = FolderItemIndex(folder);
            OpenFolderKey = null;
            FocusChanged?.Invoke(previous, FocusReason.FolderClosed);
        }

        // The folder board becomes もどる; its songs follow, with another もどる every ten songs.
        void InsertFolder(int index, Action<FolderChange> notify)
        {
            var folderItem = items[index];
            int folder = folderItem.Folder;
            var change = new FolderChange { Anchor = folderItem };
            change.Removed.Add(folderItem);
            var back = NewBack(folder);
            change.Added.Add(back);
            var songsIn = folders[folder].Songs;
            for (int i = 0; i < songsIn.Length; i++)
            {
                if (i > 0 && i % BackEvery == 0) change.Added.Add(NewBack(folder));
                change.Added.Add(SongItem(songsIn[i], folder));
            }
            items[index] = back;
            items.InsertRange(index + 1, change.Added.Skip(1));
            openFolder = folder; openAt = index; openCount = change.Added.Count - 1;
            notify?.Invoke(change);
        }

        WheelItem NewBack(int folder) => new WheelItem { Kind = ItemKind.Back, Folder = folder, Genre = folders[folder].Genre };

        void CollapseFolder()
        {
            var change = new FolderChange { Anchor = items[openAt] };
            change.Removed.AddRange(items.GetRange(openAt, openCount + 1));
            var folderItem = new WheelItem { Kind = ItemKind.Folder, Folder = openFolder, Genre = folders[openFolder].Genre };
            change.Added.Add(folderItem);
            items.RemoveRange(openAt + 1, openCount);
            items[openAt] = folderItem;
            openFolder = -1; openAt = -1; openCount = 0;
            FolderChanged?.Invoke(change);
        }

        // ---------------------------------------------------------------- course select

        void EnterCourseSelect()
        {
            var courses = items[Focused].Info.Courses.Select(c => c.Difficulty).ToList();
            int preferred = Switcher.LastDifficulty;
            if (SearchActive)
            {
                var matching = items[Focused].Info.Courses.Where(SearchQuery.Matches).Select(c => c.Difficulty).ToList();
                if (matching.Count > 0 && !matching.Contains((Difficulty)preferred)) preferred = (int)matching[0];
            }
            Cursor = new DifficultyCursor(courses, SearchActive ? preferred == (int)Difficulty.Ura : Cursor != null && Cursor.IsUra, preferred);
            SetPhase(State.CourseSelect);
        }

        void ExitCourseSelect() => SetPhase(State.Browsing);

        // ---------------------------------------------------------------- play options

        // SongSelectPlayer::handle_input_selecting: don on the option button opens ModifierSelector.
        void OpenOptions()
        {
            OptionMenu = new OptionMenu(PlayOptions.Shared, hitSoundCount);
            OptionsOpened?.Invoke(OptionMenu);
            VoiceRequested?.Invoke(Voice.Options);
        }

        public void CloseOptions()
        {
            if (!AcceptsInput() || !IsOptionPanelOpen || IsOptionsClosing) return;
            Play(Sound.Don);
            OptionMenu.ConfirmAll();
            OptionsClosing?.Invoke();
        }

        // Player::update: the options are saved once the panel has slid out.
        public void OptionsHidden()
        {
            if (OptionMenu == null) return;
            OptionMenu = null;
            PlayOptions.Shared.Save();
        }

        void ChangeOption(int direction)
        {
            if (!(direction < 0 ? OptionMenu.Left() : OptionMenu.Right())) return;
            OptionChanged?.Invoke(direction);
        }

        // A tap on an option row (direction 0) or on one of its arrows (-1/+1).
        public void TapOptionRow(int row, int direction)
        {
            if (!AcceptsInput() || !IsOptionPanelOpen || IsOptionsClosing) return;
            if (direction == 0)
            {
                if (OptionMenu.Index == row) Confirm();
                else { Play(Sound.Ka); OptionMenu.Select(row); }
                return;
            }
            OptionMenu.Select(row);
            if (direction < 0) Left(); else Right();
        }
    }
}
