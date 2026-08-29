using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
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
                var defaultNote = new NoteItem
                {
                    X = 150,
                    Y = 150,
                    Width = 280,
                    Height = 320,
                    ColorTheme = "Yellow",
                    PlainText = "ようこそ fusen へ！\n- 上部バーをダブルクリックで折りたたみ\n- Ctrl + V で画像を直接貼り付け\n- ＋ ボタンで新規付箋を追加\n- 📋 ボタンで一覧マネージャーを表示",
                    IsVisible = true
                };
                defaultNote.UpdateTitleFromPlainText();
                Notes.Add(defaultNote);
            }
            else
            {
                foreach (var note in loadedNotes)
                {
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
            double newX = x ?? (100 + (Notes.Count % 8) * 30);
            double newY = y ?? (100 + (Notes.Count % 8) * 30);

            // デスクトップの表示領域内に収める
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            if (newX > screenWidth - 300) newX = 100;
            if (newY > screenHeight - 340) newY = 100;

            var note = new NoteItem
            {
                X = newX,
                Y = newY,
                Width = 280,
                Height = 300,
                ColorTheme = "Yellow",
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
            CloseNoteWindow(note.Id);
            Notes.Remove(note);
            RequestAutoSave();
            NotesChanged?.Invoke();
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
            foreach (var kvp in _openWindows.ToList())
            {
                kvp.Value.Close();
            }
            _openWindows.Clear();
            _listWindow?.Close();
        }
    }
}
