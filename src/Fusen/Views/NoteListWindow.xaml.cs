using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Fusen.Models;
using Fusen.Services;

namespace Fusen.Views
{
    public partial class NoteListWindow : Window
    {
        private ICollectionView _notesView;

        public NoteListWindow()
        {
            InitializeComponent();

            _notesView = CollectionViewSource.GetDefaultView(NoteManager.Instance.Notes);
            _notesView.Filter = FilterNotes;
            _notesView.SortDescriptions.Add(new SortDescription(nameof(NoteItem.UpdatedAt), ListSortDirection.Descending));

            NotesListBox.ItemsSource = _notesView;
            UpdateCountDisplay();

            NoteManager.Instance.NotesChanged += OnNotesChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            NoteManager.Instance.NotesChanged -= OnNotesChanged;
        }

        private void OnNotesChanged()
        {
            Dispatcher.Invoke(() =>
            {
                _notesView.Refresh();
                UpdateCountDisplay();
            });
        }

        private bool FilterNotes(object obj)
        {
            if (obj is not NoteItem note) return false;

            var filter = SearchTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(filter)) return true;

            return (note.Title?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (note.PlainText?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private void UpdateCountDisplay()
        {
            var count = NoteManager.Instance.Notes.Count;
            NotesCountTextBlock.Text = $"全 {count} 件のメモ";
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _notesView.Refresh();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            var newNote = NoteManager.Instance.CreateNewNote();
            NoteManager.Instance.FocusNote(newNote);
        }

        private void BtnFoldAll_Click(object sender, RoutedEventArgs e)
        {
            NoteManager.Instance.FoldAll(true);
        }

        private void BtnExpandAll_Click(object sender, RoutedEventArgs e)
        {
            NoteManager.Instance.FoldAll(false);
        }

        private void BtnOpenDataDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = StorageService.Instance.DataDirectory;
                if (Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"データフォルダを開けませんでした:\n{ex.Message}", "fusen", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void NotesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (NotesListBox.SelectedItem is NoteItem note)
            {
                NoteManager.Instance.FocusNote(note);
            }
        }

        private void BtnFocusItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NoteItem note)
            {
                NoteManager.Instance.FocusNote(note);
            }
        }

        private void BtnToggleVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NoteItem note)
            {
                NoteManager.Instance.ToggleNoteVisibility(note);
            }
        }

        private void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NoteItem note)
            {
                var result = MessageBox.Show(
                    $"付箋「{note.Title}」を削除しますか？\n（この操作は取り消せません）",
                    "fusen - 付箋の削除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    NoteManager.Instance.DeleteNote(note);
                }
            }
        }
    }
}
