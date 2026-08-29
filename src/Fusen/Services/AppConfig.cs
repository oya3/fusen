using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Media;
using Fusen.Models;

namespace Fusen.Services
{
    public class AppConfig
    {
        private static AppConfig? _instance;
        public static AppConfig Instance => _instance ??= new AppConfig();

        public string ConfigFilePath { get; private set; } = string.Empty;

        // [Note] 設定項目
        public double DefaultWidth { get; set; } = 280;
        public double DefaultHeight { get; set; } = 300;
        public string DefaultColorTheme { get; set; } = "Yellow";
        public double DefaultOpacity { get; set; } = 1.0;
        public bool DefaultPinned { get; set; } = false;
        public double FontSize { get; set; } = 13.5;
        public string FontFamily { get; set; } = "Segoe UI, Yu Gothic UI, Meiryo";

        // [Backup] 設定項目
        public bool BackupEnabled { get; set; } = true;
        public int BackupGenerations { get; set; } = 5;
        public int BackupIntervalMinutes { get; set; } = 10;

        public void Initialize()
        {
            // 設定ファイルの検索とロード
            ConfigFilePath = LocateConfigFilePath();

            if (!File.Exists(ConfigFilePath))
            {
                try
                {
                    File.WriteAllText(ConfigFilePath, GetDefaultIniTemplate(), Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AppConfig] Failed to create default ini at {ConfigFilePath}: {ex.Message}");
                }
            }

            LoadFromIni();
        }

        private string LocateConfigFilePath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var iniAtBase = Path.Combine(baseDir, "fusen.ini");
            if (File.Exists(iniAtBase))
            {
                return iniAtBase;
            }

            // 開発環境 (src/Fusen/bin/...等) の場合はプロジェクトルートを確認
            var devIni = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "fusen.ini"));
            if (File.Exists(devIni))
            {
                return devIni;
            }

            // どちらにも存在しない場合、開発環境ディレクトリが存在すればプロジェクトルートを優先
            var devRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            if (Directory.Exists(Path.Combine(devRoot, "src")))
            {
                return devIni;
            }

            return iniAtBase;
        }

        public void LoadFromIni()
        {
            if (!File.Exists(ConfigFilePath)) return;

            var ini = IniFile.Load(ConfigFilePath);

            // [Note] または [General] セクション
            string noteSection = ini.ContainsSection("Note") ? "Note" : (ini.ContainsSection("General") ? "General" : "Note");

            DefaultWidth = ini.GetDouble(noteSection, "Width", 280, 200, 2000);
            DefaultHeight = ini.GetDouble(noteSection, "Height", 300, 120, 2000);

            var themeStr = ini.GetString(noteSection, "ColorTheme");
            if (string.IsNullOrWhiteSpace(themeStr))
            {
                themeStr = ini.GetString(noteSection, "Color", "Yellow");
            }
            DefaultColorTheme = string.IsNullOrWhiteSpace(themeStr) ? "Yellow" : themeStr;

            DefaultOpacity = ini.GetOpacity(noteSection, "Opacity", 1.0);
            DefaultPinned = ini.GetBool(noteSection, "Pinned", false);
            FontSize = ini.GetDouble(noteSection, "FontSize", 13.5, 9.0, 36.0);

            var fontFam = ini.GetString(noteSection, "FontFamily");
            if (!string.IsNullOrWhiteSpace(fontFam))
            {
                FontFamily = fontFam;
            }

            // [Backup] セクション
            BackupEnabled = ini.GetBool("Backup", "Enabled", true);
            BackupGenerations = ini.GetInt("Backup", "Generations", 5, 1, 100);
            BackupIntervalMinutes = ini.GetInt("Backup", "IntervalMinutes", 10, 1, 1440);

            // テーマの初期化・カスタムテーマ登録
            NoteColorTheme.InitializeThemes(ini);
        }

        public void OpenConfigFileInEditor()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    File.WriteAllText(ConfigFilePath, GetDefaultIniTemplate(), Encoding.UTF8);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = ConfigFilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppConfig] OpenConfigFileInEditor error: {ex.Message}");
                throw;
            }
        }

        public static string GetDefaultIniTemplate()
        {
            return @"; ==============================================================================
; fusen (デスクトップ付箋) 設定ファイル
; ==============================================================================
; このファイルは fusen.exe と同じフォルダに配置して読み込みます。
; 値を変更した後は、アプリを再起動すると反映されます。

[Note]
; 新規作成時の付箋のデフォルト幅（ピクセル単位、最小 200）
Width = 280

; 新規作成時の付箋のデフォルト高さ（ピクセル単位、最小 120）
Height = 300

; 新規作成時のデフォルトテーマカラー
; 利用可能な標準テーマ: Yellow, Green, Blue, Pink, Purple, Dark, Custom
ColorTheme = Yellow

; 付箋ウィンドウ全体の不透明度 (0.10 〜 1.00、または 10 〜 100 %)
; 例: 0.85 または 85 に設定すると、付箋全体が 85% の半透明になります。
Opacity = 1.0

; 新規作成時に最前面に固定（ピン留め）するかどうか (true / false)
Pinned = false

; 本文のフォントサイズ (標準: 13.5)
FontSize = 13.5

; 本文のフォントファミリー (省略時はシステムの既定フォント)
FontFamily = Segoe UI, Yu Gothic UI, Meiryo

; ------------------------------------------------------------------------------
; [Backup] 世代バックアップ設定
; data/notes.json のコピーを data/backups/ に世代保存します。
; 万一 notes.json が破損しても、起動時に最新の正常な世代から自動復旧します。
; ------------------------------------------------------------------------------
[Backup]
; 世代バックアップを有効にするかどうか (true / false)
Enabled = true

; 保持する世代数 (1 〜 100)。超過した古い世代から自動削除されます。
Generations = 5

; 稼働中にバックアップを作成する間隔（分, 1 〜 1440）
; 起動時には、この間隔に関わらず必ず 1 世代作成されます。
IntervalMinutes = 10

; ------------------------------------------------------------------------------
; [CustomTheme] カスタムカラー設定
; ColorTheme = Custom を指定した際、または独自の色を定義したい場合に使用します。
; 
; 【半透明（透過）の指定について】
; 色は #RRGGBB（通常）または #AARRGGBB（半透明: 先頭2桁AAがアルファ透過度）で指定できます。
; 例:
;   #D9FFFDE7 : 約85% 不透明のイエロー（背景が透けてデスクトップが見えます）
;   #80000000 : 50% 半透明のブラック
;   #B3E8F5E9 : 70% 半透明のミントグリーン
; ------------------------------------------------------------------------------
[CustomTheme]
; カスタムテーマの表示名（カラーパレットのツールチップに表示されます）
DisplayName = カスタム

; 付箋の背景色 (#RRGGBB または #AARRGGBB)
Background = #FFFDE7

; ヘッダー（上部タイトルバー）の背景色
Header = #FFF59D

; 枠線の色
Border = #FFF176

; 本文の文字色
Foreground = #212121

; ヘッダーの文字色
HeaderForeground = #3E2723

; ------------------------------------------------------------------------------
; 既存テーマの個別カスタマイズ（任意）
; 各テーマの背景色に半透明 (#AARRGGBB) を指定することも可能です。
; ------------------------------------------------------------------------------
; [Theme.Yellow]
; Background = #E6FFFDE7
; Header = #F2FFF59D
;
; [Theme.Dark]
; Background = #D92B2D30
; Header = #E61E1F22
";
        }
    }
}
