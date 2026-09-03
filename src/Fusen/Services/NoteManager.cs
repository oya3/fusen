using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Fusen.Helpers;
using Fusen.Models;
using Fusen.Views;

namespace Fusen.Services
{
    public class NoteManager
    {
        private static NoteManager? _instance;
        public static NoteManager Instance => _instance ??= new NoteManager();

        public ObservableCollection<NoteItem> Notes { get; } = new();
        private readonly Dictionary<string, NoteWindow> _openWindows = new();
        private NoteListWindow? _listWindow;
        private readonly DispatcherTimer _autoSaveTimer;

        public event Action? NotesChanged;

        public NoteManager()
        {
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _autoSaveTimer.Tick += (s, e) =>
            {
                _autoSaveTimer.Stop();
                SaveAllImmediately();
            };
        }

        public void Initialize()
        {
            var loadedNotes = StorageService.Instance.LoadNotes();
            Notes.Clear();

            if (loadedNotes.Count == 0)
            {
                // 初回起動時のウェルカム付箋を作成
                var config = AppConfig.Instance;
                var defaultNote = new NoteItem
                {
                    X = 150,
                    Y = 150,
                    Width = config.DefaultWidth,
                    Height = Math.Max(320, config.DefaultHeight),
                    ColorTheme = config.DefaultColorTheme,
                    Opacity = config.DefaultOpacity,
                    FontSize = config.FontSize,
                    IsPinned = config.DefaultPinned,
                    PlainText = "ようこそ fusen へ！\n- 上部バーをダブルクリックで折りたたみ\n- Ctrl + V で画像を直接貼り付け\n- ＋ ボタンで新規付箋を追加\n- Ctrl + L またはトレイアイコンで一覧マネージャーを表示",
                    IsVisible = true
                };
                defaultNote.UpdateTitleFromPlainText();
                Notes.Add(defaultNote);
            }
            else
            {
                foreach (var note in loadedNotes)
                {
                    // title は plainText の派生値。notes.json 内では plainText より後ろに
                    // 位置するため、読み込むと保存済みの title が復元後の値を上書きしてしまう。
                    // 古い規則で切り詰められた title が残り続けないよう、必ず作り直す。
                    note.UpdateTitleFromPlainText();
                    Notes.Add(note);
                }
            }

            // 表示対象の付箋ウィンドウをオープン
            foreach (var note in Notes)
            {
                if (note.IsVisible)
                {
                    ShowNoteWindow(note);
                }
            }

            // 破損からの復旧が発生していれば、付箋を表示し終えてから通知する
            NotifyIfRecovered();
        }

        /// <summary>
        /// 起動時に notes.json の復旧が発生していた場合に、その結果をユーザーへ知らせる。
        /// </summary>
        /// <summary>
        /// 起動時に notes.json の復旧が発生していた場合に、その結果をユーザーへ知らせる。
        /// </summary>
        private static void NotifyIfRecovered()
        {
            var recovery = StorageService.Instance.LastRecovery;
            if (recovery == null) return;

            string message;
            string caption;
            MessageBoxImage icon;

            if (recovery.Succeeded)
            {
                caption = "fusen - バックアップから復元しました";
                icon = MessageBoxImage.Information;
                message = "付箋データ (notes.json) が読み込めなかったため、バックアップから復元しました。\n\n"
                        + $"復元元: {recovery.RecoveredFromFileName}\n"
                        + $"復元した付箋: {recovery.NoteCount} 件";
            }
            else
            {
                caption = "fusen - データを読み込めませんでした";
                icon = MessageBoxImage.Warning;
                message = "付箋データ (notes.json) が読み込めませんでした。\n"
                        + "利用可能なバックアップも見つからなかったため、空の状態で起動します。";
            }

            if (!string.IsNullOrEmpty(recovery.QuarantinedFileName))
            {
                message += "\n\n読み込めなかったファイルは削除せず、data/backups/ に次の名前で保管しています。\n"
                        + recovery.QuarantinedFileName;
            }

            try
            {
                MessageBox.Show(message, caption, MessageBoxButton.OK, icon);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteManager] NotifyIfRecovered error: {ex.Message}");
            }
        }

        public void RequestAutoSave()
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        public void SaveAllImmediately()
        {
            // 開いている全ウィンドウの現在位置やサイズを NoteItem に同期
            foreach (var kvp in _openWindows)
            {
                var window = kvp.Value;
                window.SyncModelFromWindow();
            }

            StorageService.Instance.SaveNotes(Notes.ToList());
            NotesChanged?.Invoke();
        }

        public NoteItem CreateNewNote(double? x = null, double? y = null, string? initialText = null)
        {
            var config = AppConfig.Instance;
            double newX = x ?? (100 + (Notes.Count % 8) * 30);
            double newY = y ?? (100 + (Notes.Count % 8) * 30);

            // 表示位置は全モニタを含む仮想デスクトップの内側へ寄せる
            (newX, newY) = ScreenHelper.ClampToVirtualScreen(newX, newY, config.DefaultWidth, config.DefaultHeight);

            var note = new NoteItem
            {
                X = newX,
                Y = newY,
                Width = config.DefaultWidth,
                Height = config.DefaultHeight,
                ColorTheme = config.DefaultColorTheme,
                Opacity = config.DefaultOpacity,
                FontSize = config.FontSize,
                IsPinned = config.DefaultPinned,
                PlainText = initialText ?? string.Empty,
                IsVisible = true
            };
            note.UpdateTitleFromPlainText();

            Notes.Add(note);
            ShowNoteWindow(note);
            RequestAutoSave();
            NotesChanged?.Invoke();

            return note;
        }

        public void ShowNoteWindow(NoteItem note)
        {
            if (_openWindows.TryGetValue(note.Id, out var existingWindow))
            {
                if (!existingWindow.IsVisible)
                {
                    existingWindow.Show();
                }
                existingWindow.Activate();
                return;
            }

            var window = new NoteWindow(note);
            _openWindows[note.Id] = window;
            note.IsVisible = true;
            window.Show();
        }

        public void CloseNoteWindow(string noteId)
        {
            if (_openWindows.TryGetValue(noteId, out var window))
            {
                window.SyncModelFromWindow();
                window.Close();
                _openWindows.Remove(noteId);
            }
        }

        public void DeleteNote(NoteItem note)
        {
            // 付箋を外す前に調べる。外した後では「他の付箋も使っているか」を正しく判定できない。
            var orphanedImages = GetImagesOnlyUsedBy(note);

            CloseNoteWindow(note.Id);
            Notes.Remove(note);

            foreach (var fullPath in orphanedImages)
            {
                StorageService.Instance.DeleteImageFile(fullPath);
            }

            RequestAutoSave();
            NotesChanged?.Invoke();
        }

        /// <summary>
        /// この付箋だけが参照している画像ファイルを返す（実在するもののみ）。
        ///
        /// 記法はテキストなのでコピーできる。同じ画像を他の付箋も指している場合は、
        /// この付箋を消してもファイルは残さなければならない。
        /// 削除前の確認ダイアログで枚数を示すためにも使う。
        /// </summary>
        public List<string> GetImagesOnlyUsedBy(NoteItem note)
        {
            var files = CollectImageFiles(note);
            if (files.Count == 0) return new List<string>();

            foreach (var other in Notes)
            {
                if (ReferenceEquals(other, note)) continue;
                files.ExceptWith(CollectImageFiles(other));
            }

            return files.ToList();
        }

        /// <summary>
        /// どの付箋からも参照されなくなった画像ファイルを削除する。
        ///
        /// 本文から画像記法を1つ消したときに使う。同じ画像を別の付箋が指していたり、
        /// 同じ付箋の別の行にもう一度書かれていたりする場合はファイルを残す。
        /// 呼ぶ側は、記法を消した結果を付箋の本文へ反映してから呼ぶこと。
        /// </summary>
        public bool DeleteImageIfUnreferenced(string? markdownPath)
        {
            if (string.IsNullOrWhiteSpace(markdownPath)) return false;

            var fullPath = StorageService.Instance.ResolveImagePath(markdownPath);
            if (string.IsNullOrEmpty(fullPath)) return false;

            foreach (var note in Notes)
            {
                if (CollectImageFiles(note).Contains(fullPath)) return false;
            }

            return StorageService.Instance.DeleteImageFile(fullPath);
        }

        /// <summary>
        /// 付箋が参照している画像ファイルの実パスを集める。
        /// 本文と保存用 XAML の両方を見るのは、どちらか一方が古い状態でも取りこぼさないため。
        /// </summary>
        private static HashSet<string> CollectImageFiles(NoteItem note)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var text in new[] { note.PlainText, note.ContentXaml })
            {
                foreach (var path in FlowDocumentHelper.EnumerateImagePaths(text))
                {
                    var fullPath = StorageService.Instance.ResolveImagePath(path);
                    if (!string.IsNullOrEmpty(fullPath))
                    {
                        files.Add(fullPath);
                    }
                }
            }

            return files;
        }

        public void ToggleNoteVisibility(NoteItem note)
        {
            if (note.IsVisible)
            {
                // デスクトップから剥がす（非表示・保管）
                CloseNoteWindow(note.Id);
                note.IsVisible = false;
            }
            else
            {
                // デスクトップに再表示
                note.IsVisible = true;
                ShowNoteWindow(note);
            }
            RequestAutoSave();
            NotesChanged?.Invoke();
        }

        public void FocusNote(NoteItem note)
        {
            if (!note.IsVisible)
            {
                note.IsVisible = true;
            }

            ShowNoteWindow(note);

            if (_openWindows.TryGetValue(note.Id, out var window))
            {
                if (note.IsFolded)
                {
                    window.ToggleFold(false);
                }

                // 画面外にある付箋を「開く」ときは見える位置へ引き戻す。
                // そのまま表示しても操作できず、一覧から辿り着く手段が無くなるため。
                window.EnsureOnScreen();

                window.Activate();
                window.Topmost = true;
                if (!note.IsPinned)
                {
                    // ピン留めでない場合は一時的に最前面にしてから戻す
                    window.Topmost = false;
                }
            }
        }

        public void FoldAll(bool fold)
        {
            foreach (var kvp in _openWindows)
            {
                kvp.Value.ToggleFold(fold);
            }
            RequestAutoSave();
        }

        public void ShowNoteList()
        {
            if (_listWindow == null || !_listWindow.IsLoaded)
            {
                _listWindow = new NoteListWindow();
                _listWindow.Closed += (s, e) => _listWindow = null;
                _listWindow.Show();
            }
            else
            {
                _listWindow.Activate();
            }
        }

        public void Shutdown()
        {
            SaveAllImmediately();

            // 終了時の状態を1世代残しておく（次回起動までの間に破損しても直前まで戻せる）
            StorageService.Instance.BackupNow();
            foreach (var kvp in _openWindows.ToList())
            {
                kvp.Value.Close();
            }
            _openWindows.Clear();
            _listWindow?.Close();
        }
    }
}
