using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;

namespace Fusen.Services
{
    public class IniFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new(StringComparer.OrdinalIgnoreCase);

        public string FilePath { get; private set; } = string.Empty;

        public IniFile()
        {
        }

        public static IniFile Load(string filePath)
        {
            var ini = new IniFile { FilePath = filePath };
            if (!File.Exists(filePath))
            {
                return ini;
            }

            try
            {
                string currentSection = "General";
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);

                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();

                    // 空行または行頭コメントのスキップ
                    if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#'))
                    {
                        continue;
                    }

                    // セクションヘッダー: [SectionName]
                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        currentSection = line.Substring(1, line.Length - 2).Trim();
                        if (!ini._sections.ContainsKey(currentSection))
                        {
                            ini._sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        }
                        continue;
                    }

                    // 行末コメント（;以降）の除去
                    int commentIndex = line.IndexOf(';');
                    if (commentIndex >= 0)
                    {
                        line = line.Substring(0, commentIndex).Trim();
                    }

                    // Key = Value または Key : Value のパース
                    int eqIndex = line.IndexOf('=');
                    if (eqIndex < 0)
                    {
                        eqIndex = line.IndexOf(':');
                    }

                    if (eqIndex > 0)
                    {
                        string key = line.Substring(0, eqIndex).Trim();
                        string value = line.Substring(eqIndex + 1).Trim();

                        // 引用符（" または '）の除去
                        if ((value.StartsWith('"') && value.EndsWith('"')) ||
                            (value.StartsWith('\'') && value.EndsWith('\'')))
                        {
                            if (value.Length >= 2)
                            {
                                value = value.Substring(1, value.Length - 2);
                            }
                        }

                        if (!ini._sections.TryGetValue(currentSection, out var dict))
                        {
                            dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            ini._sections[currentSection] = dict;
                        }
                        dict[key] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IniFile] Load error from {filePath}: {ex.Message}");
            }

            return ini;
        }

        public bool ContainsSection(string section) => _sections.ContainsKey(section);

        public IEnumerable<string> GetSections() => _sections.Keys;

        public Dictionary<string, string> GetSection(string section)
        {
            if (_sections.TryGetValue(section, out var dict))
            {
                return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
            }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string GetString(string section, string key, string defaultValue = "")
        {
            if (_sections.TryGetValue(section, out var dict) && dict.TryGetValue(key, out var val))
            {
                return string.IsNullOrWhiteSpace(val) ? defaultValue : val;
            }
            return defaultValue;
        }

        public double GetDouble(string section, string key, double defaultValue, double min = double.MinValue, double max = double.MaxValue)
        {
            var str = GetString(section, key);
            if (string.IsNullOrWhiteSpace(str)) return defaultValue;

            if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                return Math.Clamp(result, min, max);
            }
            return defaultValue;
        }

        public int GetInt(string section, string key, int defaultValue, int min = int.MinValue, int max = int.MaxValue)
        {
            var str = GetString(section, key);
            if (string.IsNullOrWhiteSpace(str)) return defaultValue;

            if (int.TryParse(str.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            {
                return Math.Clamp(result, min, max);
            }
            return defaultValue;
        }

        public double GetOpacity(string section, string key, double defaultValue = 1.0)
        {
            var str = GetString(section, key);
            if (string.IsNullOrWhiteSpace(str)) return defaultValue;

            str = str.Trim().TrimEnd('%');
            if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                if (result > 1.0 && result <= 100.0)
                {
                    result /= 100.0;
                }
                return Math.Clamp(result, 0.1, 1.0);
            }
            return defaultValue;
        }

        public bool GetBool(string section, string key, bool defaultValue = false)
        {
            var str = GetString(section, key);
            if (string.IsNullOrWhiteSpace(str)) return defaultValue;

            str = str.Trim().ToLowerInvariant();
            if (str is "true" or "1" or "yes" or "on" or "y") return true;
            if (str is "false" or "0" or "no" or "off" or "n") return false;
            return defaultValue;
        }

        public Color GetColor(string section, string key, Color defaultColor)
        {
            var str = GetString(section, key);
            if (string.IsNullOrWhiteSpace(str)) return defaultColor;

            return ParseColorString(str, defaultColor);
        }

        public static Color ParseColorString(string colorStr, Color defaultColor)
        {
            if (string.IsNullOrWhiteSpace(colorStr)) return defaultColor;
            colorStr = colorStr.Trim();

            // #無しの16進数文字列 (例: FFFDE7 や CCFFFDE7) に # を自動補完
            if (!colorStr.StartsWith("#") && !colorStr.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(colorStr, "^[0-9a-fA-F]{3,8}$"))
                {
                    colorStr = "#" + colorStr;
                }
            }

            try
            {
                var converted = ColorConverter.ConvertFromString(colorStr);
                if (converted is Color c)
                {
                    return c;
                }
            }
            catch
            {
            }

            return defaultColor;
        }
    }
}
