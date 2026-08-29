using System;
using System.Collections.Generic;
using System.Windows.Media;
using Fusen.Services;

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
            RegisterDefaultThemes();
        }

        public static void InitializeThemes(IniFile? ini = null)
        {
            Themes.Clear();
            RegisterDefaultThemes();

            if (ini == null) return;

            // [CustomTheme] の読み込み
            if (ini.ContainsSection("CustomTheme"))
            {
                var customTheme = CreateThemeFromSection(ini, "CustomTheme", "Custom", "カスタム", "#FFFDE7", "#FFF59D", "#FFF176", "#212121", "#3E2723");
                RegisterTheme(customTheme);
            }

            // [Theme.<Name>] セクションの読み込み（Yellow等の上書きまたは新規テーマ追加）
            foreach (var section in ini.GetSections())
            {
                if (section.StartsWith("Theme.", StringComparison.OrdinalIgnoreCase) && section.Length > 6)
                {
                    string themeName = section.Substring(6).Trim();
                    if (!string.IsNullOrEmpty(themeName))
                    {
                        var baseTheme = Themes.TryGetValue(themeName, out var existing) ? existing : null;
                        var theme = CreateThemeFromSection(ini, section, themeName, baseTheme?.DisplayName ?? themeName,
                            baseTheme != null ? GetBrushHex(baseTheme.BackgroundBrush) : "#FFFDE7",
                            baseTheme != null ? GetBrushHex(baseTheme.HeaderBrush) : "#FFF59D",
                            baseTheme != null ? GetBrushHex(baseTheme.BorderBrush) : "#FFF176",
                            baseTheme != null ? GetBrushHex(baseTheme.ForegroundBrush) : "#212121",
                            baseTheme != null ? GetBrushHex(baseTheme.HeaderForegroundBrush) : "#3E2723");
                        RegisterTheme(theme);
                    }
                }
            }
        }

        private static void RegisterDefaultThemes()
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

        private static ColorThemeInfo CreateThemeFromSection(IniFile ini, string section, string defaultName, string defaultDisplayName,
            string defaultBg, string defaultHeader, string defaultBorder, string defaultFg, string defaultHeaderFg)
        {
            string name = ini.GetString(section, "Name", defaultName);
            string displayName = ini.GetString(section, "DisplayName", defaultDisplayName);

            string bgHex = ini.GetString(section, "Background", defaultBg);
            string headerHex = ini.GetString(section, "Header", defaultHeader);
            string borderHex = ini.GetString(section, "Border", defaultBorder);
            string fgHex = ini.GetString(section, "Foreground", defaultFg);
            string headerFgHex = ini.GetString(section, "HeaderForeground", defaultHeaderFg);

            var bgBrush = CreateBrush(bgHex);
            var headerBrush = CreateBrush(headerHex);
            var borderBrush = CreateBrush(borderHex);
            var fgBrush = CreateBrush(fgHex);
            var headerFgBrush = CreateBrush(headerFgHex);
            var buttonHover = CreateBrush(borderHex, 0.5);
            var iconBrush = headerFgBrush;

            return new ColorThemeInfo
            {
                Name = name,
                DisplayName = displayName,
                BackgroundBrush = bgBrush,
                HeaderBrush = headerBrush,
                BorderBrush = borderBrush,
                ForegroundBrush = fgBrush,
                HeaderForegroundBrush = headerFgBrush,
                ButtonHoverBrush = buttonHover,
                IconColorBrush = iconBrush
            };
        }

        public static void RegisterTheme(ColorThemeInfo theme)
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

        public static SolidColorBrush CreateBrush(string hexColor, double opacity = 1.0)
        {
            var color = IniFile.ParseColorString(hexColor, Colors.Transparent);
            if (opacity < 1.0)
            {
                color.A = (byte)(color.A * Math.Clamp(opacity, 0.0, 1.0));
            }
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static string GetBrushHex(SolidColorBrush brush)
        {
            var c = brush.Color;
            if (c.A == 255)
            {
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        public static ColorThemeInfo GetTheme(string? themeName)
        {
            if (!string.IsNullOrWhiteSpace(themeName) && Themes.TryGetValue(themeName, out var theme))
            {
                return theme;
            }
            if (Themes.TryGetValue("Yellow", out var yellow))
            {
                return yellow;
            }
            foreach (var t in Themes.Values)
            {
                return t;
            }
            return new ColorThemeInfo();
        }

        public static IEnumerable<ColorThemeInfo> GetAllThemes() => Themes.Values;
    }
}
