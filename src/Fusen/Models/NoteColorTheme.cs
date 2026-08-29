using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Fusen.Models
{
    public class ColorThemeInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public SolidColorBrush BackgroundBrush { get; set; } = Brushes.Transparent;
        public SolidColorBrush HeaderBrush { get; set; } = Brushes.Transparent;
        public SolidColorBrush BorderBrush { get; set; } = Brushes.Transparent;
        public SolidColorBrush ForegroundBrush { get; set; } = Brushes.Black;
        public SolidColorBrush HeaderForegroundBrush { get; set; } = Brushes.Black;
        public SolidColorBrush ButtonHoverBrush { get; set; } = Brushes.Transparent;
        public SolidColorBrush IconColorBrush { get; set; } = Brushes.Black;
    }

    public static class NoteColorTheme
    {
        private static readonly Dictionary<string, ColorThemeInfo> Themes = new(StringComparer.OrdinalIgnoreCase);

        static NoteColorTheme()
        {
            RegisterTheme(new ColorThemeInfo
            {
                Name = "Yellow",
                DisplayName = "イエロー",
                BackgroundBrush = CreateBrush("#FFFDE7"),
                HeaderBrush = CreateBrush("#FFF59D"),
                BorderBrush = CreateBrush("#FFF176"),
                ForegroundBrush = CreateBrush("#212121"),
                HeaderForegroundBrush = CreateBrush("#3E2723"),
                ButtonHoverBrush = CreateBrush("#FFF176", 0.5),
                IconColorBrush = CreateBrush("#5D4037")
            });

            RegisterTheme(new ColorThemeInfo
            {
                Name = "Green",
                DisplayName = "グリーン",
                BackgroundBrush = CreateBrush("#E8F5E9"),
                HeaderBrush = CreateBrush("#C8E6C9"),
                BorderBrush = CreateBrush("#A5D6A7"),
                ForegroundBrush = CreateBrush("#212121"),
                HeaderForegroundBrush = CreateBrush("#1B5E20"),
                ButtonHoverBrush = CreateBrush("#A5D6A7", 0.5),
                IconColorBrush = CreateBrush("#2E7D32")
            });

            RegisterTheme(new ColorThemeInfo
            {
                Name = "Blue",
                DisplayName = "ブルー",
                BackgroundBrush = CreateBrush("#E1F5FE"),
                HeaderBrush = CreateBrush("#B3E5FC"),
                BorderBrush = CreateBrush("#81D4FA"),
                ForegroundBrush = CreateBrush("#212121"),
                HeaderForegroundBrush = CreateBrush("#01579B"),
                ButtonHoverBrush = CreateBrush("#81D4FA", 0.5),
                IconColorBrush = CreateBrush("#0277BD")
            });

            RegisterTheme(new ColorThemeInfo
            {
                Name = "Pink",
                DisplayName = "ピンク",
                BackgroundBrush = CreateBrush("#FCE4EC"),
                HeaderBrush = CreateBrush("#F8BBD0"),
                BorderBrush = CreateBrush("#F48FB1"),
                ForegroundBrush = CreateBrush("#212121"),
                HeaderForegroundBrush = CreateBrush("#880E4F"),
                ButtonHoverBrush = CreateBrush("#F48FB1", 0.5),
                IconColorBrush = CreateBrush("#C2185B")
            });

            RegisterTheme(new ColorThemeInfo
            {
                Name = "Purple",
                DisplayName = "パープル",
                BackgroundBrush = CreateBrush("#F3E5F5"),
                HeaderBrush = CreateBrush("#E1BEE7"),
                BorderBrush = CreateBrush("#CE93D8"),
                ForegroundBrush = CreateBrush("#212121"),
                HeaderForegroundBrush = CreateBrush("#4A148C"),
                ButtonHoverBrush = CreateBrush("#CE93D8", 0.5),
                IconColorBrush = CreateBrush("#7B1FA2")
            });

            RegisterTheme(new ColorThemeInfo
            {
                Name = "Dark",
                DisplayName = "ダーク",
                BackgroundBrush = CreateBrush("#2B2D30"),
                HeaderBrush = CreateBrush("#1E1F22"),
                BorderBrush = CreateBrush("#3C3F41"),
                ForegroundBrush = CreateBrush("#E0E0E0"),
                HeaderForegroundBrush = CreateBrush("#FFFFFF"),
                ButtonHoverBrush = CreateBrush("#3C3F41", 0.7),
                IconColorBrush = CreateBrush("#CCCCCC")
            });
        }

        private static void RegisterTheme(ColorThemeInfo theme)
        {
            theme.BackgroundBrush.Freeze();
            theme.HeaderBrush.Freeze();
            theme.BorderBrush.Freeze();
            theme.ForegroundBrush.Freeze();
            theme.HeaderForegroundBrush.Freeze();
            theme.ButtonHoverBrush.Freeze();
            theme.IconColorBrush.Freeze();
            Themes[theme.Name] = theme;
        }

        private static SolidColorBrush CreateBrush(string hexColor, double opacity = 1.0)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            if (opacity < 1.0)
            {
                color.A = (byte)(255 * opacity);
            }
            return new SolidColorBrush(color);
        }

        public static ColorThemeInfo GetTheme(string? themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName) || !Themes.TryGetValue(themeName, out var theme))
            {
                return Themes["Yellow"];
            }
            return theme;
        }

        public static IEnumerable<ColorThemeInfo> GetAllThemes() => Themes.Values;
    }
}
