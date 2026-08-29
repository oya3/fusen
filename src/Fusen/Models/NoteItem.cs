using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Fusen.Models
{
    public class NoteItem : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private double _x = 100;
        private double _y = 100;
        private double _width = 280;
        private double _height = 300;
        private bool _isFolded = false;
        private double _foldedHeight = 42;
        private bool _isPinned = false;
        private bool _isVisible = true;
        private string _colorTheme = "Yellow";
        private string _contentXaml = string.Empty;
        private string _plainText = string.Empty;
        private string _title = "新規メモ";
        private DateTime _createdAt = DateTime.Now;
        private DateTime _updatedAt = DateTime.Now;

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        public double Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        public bool IsFolded
        {
            get => _isFolded;
            set => SetProperty(ref _isFolded, value);
        }

        public double FoldedHeight
        {
            get => _foldedHeight;
            set => SetProperty(ref _foldedHeight, value);
        }

        public bool IsPinned
        {
            get => _isPinned;
            set => SetProperty(ref _isPinned, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public string ColorTheme
        {
            get => _colorTheme;
            set => SetProperty(ref _colorTheme, value);
        }

        public string ContentXaml
        {
            get => _contentXaml;
            set => SetProperty(ref _contentXaml, value);
        }

        public string PlainText
        {
            get => _plainText;
            set
            {
                if (SetProperty(ref _plainText, value))
                {
                    UpdateTitleFromPlainText();
                }
            }
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value);
        }

        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set => SetProperty(ref _updatedAt, value);
        }

        public void UpdateTitleFromPlainText()
        {
            if (string.IsNullOrWhiteSpace(_plainText))
            {
                Title = "新規メモ";
                return;
            }

            var lines = _plainText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]))
            {
                var firstLine = lines[0].Trim();
                Title = firstLine.Length > 40 ? firstLine.Substring(0, 40) + "..." : firstLine;
            }
            else
            {
                Title = "新規メモ";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
