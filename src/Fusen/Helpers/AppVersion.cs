using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Fusen.Services;

namespace Fusen.Helpers
{
    /// <summary>
    /// アプリのバージョン情報。トレイの「ℹ️ バージョン情報」で表示する（§4.3）。
    ///
    /// git のハッシュ等は <c>Fusen.csproj</c> の EmbedGitInfo ターゲットがビルド時に
    /// <c>AssemblyMetadata</c> として埋め込む。ここでは読むだけで、実行時に git は呼ばない。
    /// 配布先に git は無く、起動時に外部プロセスを起こすと「瞬時の起動」に反するため。
    /// </summary>
    public static class AppVersion
    {
        /// <summary>アセンブリのバージョン（`Fusen.csproj` の Version）。</summary>
        public static string Version { get; } = ReadVersion();

        /// <summary>短縮コミットハッシュ。未コミットの変更を抱えたビルドは `-dirty` が付く。</summary>
        public static string Commit { get; } = ReadMetadata("GitCommit");

        /// <summary>コミット日時（ISO 形式の文字列）。</summary>
        public static string CommitDate { get; } = ReadMetadata("GitCommitDate");

        /// <summary>コミットの件名（1行目）。</summary>
        public static string CommitSubject { get; } = ReadMetadata("GitCommitSubject");

        /// <summary>実行ファイルのパス。どの配布フォルダで動いているかの確認用。</summary>
        public static string ExecutablePath { get; } = ReadExecutablePath();

        /// <summary>
        /// ビルド（発行）日時。実行ファイルの最終更新日時で代用する。
        /// 埋め込みにするとビルドのたびにアセンブリ属性が変わり、増分ビルドが効かなくなるため。
        /// </summary>
        public static DateTime? BuildTime { get; } = ReadBuildTime();

        /// <summary>バージョン情報ダイアログに表示する本文を組み立てる。</summary>
        public static string BuildSummary()
        {
            var commit = string.IsNullOrEmpty(Commit) ? "不明（git 管理外でのビルド）" : Commit;
            var lines = new System.Text.StringBuilder();

            lines.AppendLine($"fusen {Version}");
            lines.AppendLine();
            lines.AppendLine($"コミット : {commit}");
            if (!string.IsNullOrEmpty(CommitDate)) lines.AppendLine($"           {CommitDate}");
            if (!string.IsNullOrEmpty(CommitSubject)) lines.AppendLine($"           {CommitSubject}");
            if (BuildTime.HasValue) lines.AppendLine($"ビルド   : {BuildTime.Value:yyyy-MM-dd HH:mm}");
            lines.AppendLine();

            // 「どの配布フォルダのどの data を掴んでいるか」は §3.1 の探索規則で分かりにくく、
            // 不具合の報告時に最初に必要になるため併せて出す。
            lines.AppendLine($"実行ファイル : {ExecutablePath}");
            lines.AppendLine($"データ       : {SafeDataDirectory()}");

            return lines.ToString().TrimEnd();
        }

        private static string ReadVersion()
        {
            var version = typeof(AppVersion).Assembly.GetName().Version;
            return version == null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private static string ReadMetadata(string key)
        {
            try
            {
                return typeof(AppVersion).Assembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == key)?.Value ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ReadExecutablePath()
        {
            try
            {
                return Environment.ProcessPath ?? typeof(AppVersion).Assembly.Location;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static DateTime? ReadBuildTime()
        {
            try
            {
                var path = ReadExecutablePath();
                return File.Exists(path) ? File.GetLastWriteTime(path) : null;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeDataDirectory()
        {
            try
            {
                return StorageService.Instance.DataDirectory;
            }
            catch
            {
                return "不明";
            }
        }
    }
}
