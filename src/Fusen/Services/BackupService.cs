using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fusen.Services
{
    /// <summary>
    /// notes.json の世代バックアップを管理するサービス。
    ///
    /// 単一ファイル方式は高速だが、notes.json が破損すると全付箋を失うという弱点を持つ。
    /// 本サービスは data/backups/ に世代を残し、破損時に直前の健全な状態へ戻せるようにする。
    ///
    /// バックアップの作成タイミング:
    ///   1. 起動時 — 読み込みに成功した直後に1世代（そのセッションで壊す前の状態を保全）
    ///   2. 稼働中 — 自動保存の成功時に、前回から IntervalMinutes 経過していれば1世代
    ///
    /// 内容に変化がなければ世代を消費しない（notes.json の更新日時で判定）。
    /// </summary>
    public class BackupService
    {
        private static BackupService? _instance;
        public static BackupService Instance => _instance ??= new BackupService();

        public const string BackupFilePrefix = "notes_";
        public const string CorruptFilePrefix = "notes_corrupt_";

        private string _backupDirectory = string.Empty;
        private DateTime _lastBackupUtc = DateTime.MinValue;
        private DateTime _lastBackedUpSourceWriteUtc = DateTime.MinValue;

        public string BackupDirectory => _backupDirectory;

        private static bool Enabled => AppConfig.Instance.BackupEnabled;
        private static int Generations => AppConfig.Instance.BackupGenerations;
        private static int IntervalMinutes => AppConfig.Instance.BackupIntervalMinutes;

        /// <summary>
        /// バックアップ先ディレクトリ (data/backups) を確定する。StorageService から呼ばれる。
        /// </summary>
        public void Initialize(string dataDirectory)
        {
            _backupDirectory = Path.Combine(dataDirectory, "backups");

            if (!Enabled) return;

            try
            {
                Directory.CreateDirectory(_backupDirectory);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupService] Initialize error: {ex.Message}");
            }
        }

        /// <summary>
        /// 間隔条件を満たしていればバックアップを作成する。
        /// </summary>
        /// <param name="force">true の場合は間隔を無視して作成する（起動時など）。</param>
        public void MaybeBackup(string notesFilePath, bool force = false)
        {
            if (!Enabled || string.IsNullOrEmpty(_backupDirectory)) return;
            if (!File.Exists(notesFilePath)) return;

            try
            {
                if (!force && (DateTime.UtcNow - _lastBackupUtc).TotalMinutes < IntervalMinutes)
                {
                    return;
                }

                // 前回のバックアップ以降 notes.json が変化していなければ世代を消費しない
                var sourceWriteUtc = File.GetLastWriteTimeUtc(notesFilePath);
                if (sourceWriteUtc <= _lastBackedUpSourceWriteUtc)
                {
                    return;
                }

                // 空ファイルはバックアップしない（破損した状態を世代に残さないため）
                if (new FileInfo(notesFilePath).Length == 0)
                {
                    return;
                }

                Directory.CreateDirectory(_backupDirectory);

                var destination = BuildUniquePath($"{BackupFilePrefix}{DateTime.Now:yyyyMMdd_HHmmss_fff}", ".json");
                File.Copy(notesFilePath, destination, overwrite: false);

                _lastBackupUtc = DateTime.UtcNow;
                _lastBackedUpSourceWriteUtc = sourceWriteUtc;

                RotateGenerations();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupService] MaybeBackup error: {ex.Message}");
            }
        }

        /// <summary>
        /// 世代バックアップを新しい順に列挙する。復旧時は先頭から順に読み込みを試す。
        /// </summary>
        public IEnumerable<string> EnumerateBackups()
        {
            if (string.IsNullOrEmpty(_backupDirectory) || !Directory.Exists(_backupDirectory))
            {
                return Enumerable.Empty<string>();
            }

            try
            {
                // ファイル名が notes_yyyyMMdd_HHmmss_fff の固定長のため、名前の降順がそのまま新しい順になる
                return Directory.EnumerateFiles(_backupDirectory, BackupFilePrefix + "*.json")
                    .Where(f => !Path.GetFileName(f).StartsWith(CorruptFilePrefix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupService] EnumerateBackups error: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>
        /// 破損した notes.json を backups/ へ退避する。上書き復旧しても原本を失わないようにするため、
        /// 削除ではなく必ず移動して残す。
        /// </summary>
        public string QuarantineCorruptFile(string notesFilePath)
        {
            try
            {
                if (!File.Exists(notesFilePath)) return string.Empty;

                Directory.CreateDirectory(_backupDirectory);
                var destination = BuildUniquePath($"{CorruptFilePrefix}{DateTime.Now:yyyyMMdd_HHmmss_fff}", ".json");
                File.Move(notesFilePath, destination, overwrite: false);
                return destination;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupService] QuarantineCorruptFile error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 世代数の上限を超えた古いバックアップを削除する。退避した破損ファイルは対象外。
        /// </summary>
        private void RotateGenerations()
        {
            try
            {
                var backups = EnumerateBackups().ToList();
                foreach (var old in backups.Skip(Math.Max(1, Generations)))
                {
                    try
                    {
                        File.Delete(old);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BackupService] Delete {old} failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupService] RotateGenerations error: {ex.Message}");
            }
        }

        /// <summary>
        /// 同一ミリ秒に複数回作成された場合でも衝突しないパスを組み立てる。
        ///
        /// 注意: ファイル名（固定長のタイムスタンプ）の昇順が作成順と一致することが、
        /// EnumerateBackups と RotateGenerations の前提になっている。
        /// 秒精度だと同一秒の2件目以降が連番付きとなり、'.' &lt; '_' の照合順のせいで
        /// 「連番なし」が常に最古と判定される。その名前が削除で空くと次の世代が再利用し、
        /// 最新世代が最古と誤認されて即座に削除されてしまうため、ミリ秒まで含めている。
        /// </summary>
        private string BuildUniquePath(string baseName, string extension)
        {
            var path = Path.Combine(_backupDirectory, baseName + extension);
            int suffix = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(_backupDirectory, $"{baseName}_{suffix:D2}{extension}");
                suffix++;
            }
            return path;
        }
    }
}
