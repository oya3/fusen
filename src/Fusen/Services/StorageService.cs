using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Fusen.Models;

namespace Fusen.Services
{
    public class NotesDocument
    {
        public int Version { get; set; } = 1;
        public List<NoteItem> Notes { get; set; } = new();
    }

    /// <summary>
    /// notes.json の復旧が発生したことを UI 層へ伝えるための結果。
    /// 表示はストレージ層の責務ではないため、ここでは記録のみを行う。
    /// </summary>
    public class RecoveryInfo
    {
        public bool Succeeded { get; init; }
        public string RecoveredFromFileName { get; init; } = string.Empty;
        public string QuarantinedFileName { get; init; } = string.Empty;
        public int NoteCount { get; init; }
    }

    public class StorageService
    {
        /// <summary>data ディレクトリ配下の画像フォルダ名。本文に書く相対パスの先頭にも使う。</summary>
        public const string ImagesDirectoryName = "images";

        private static StorageService? _instance;
        public static StorageService Instance => _instance ??= new StorageService();

        /// <summary>
        /// 直近の LoadNotes で復旧が発生した場合の情報。発生していなければ null。
        /// </summary>
        public RecoveryInfo? LastRecovery { get; private set; }

        private readonly string _dataDirectory;
        private readonly string _imagesDirectory;
        private readonly string _notesFilePath;
        private readonly JsonSerializerOptions _jsonOptions;

        public string DataDirectory => _dataDirectory;
        public string ImagesDirectory => _imagesDirectory;

        public StorageService()
        {
            // ポータブル設計: 実行ファイル直下、またはプロジェクトルートの data ディレクトリを特定
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // 開発環境 (bin/Debug/net... 等) の場合は、上位フォルダの fusen/data を確認
            var devDataDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "data"));
            if (Directory.Exists(devDataDir) || File.Exists(Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Fusen.sln"))))
            {
                _dataDirectory = devDataDir;
            }
            else
            {
                _dataDirectory = Path.Combine(baseDir, "data");
            }

            _imagesDirectory = Path.Combine(_dataDirectory, ImagesDirectoryName);
            _notesFilePath = Path.Combine(_dataDirectory, "notes.json");

            Directory.CreateDirectory(_dataDirectory);
            Directory.CreateDirectory(_imagesDirectory);

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            BackupService.Instance.Initialize(_dataDirectory);
        }

        public List<NoteItem> LoadNotes()
        {
            if (!File.Exists(_notesFilePath))
            {
                return new List<NoteItem>();
            }

            LastRecovery = null;

            // 通常読み込み
            try
            {
                var json = File.ReadAllText(_notesFilePath);

                // 空ファイルは書き込み途中の破損とみなし、世代バックアップからの復旧を試みる
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var doc = JsonSerializer.Deserialize<NotesDocument>(json, _jsonOptions);
                    if (doc?.Notes != null)
                    {
                        // 読み込みに成功した時点の状態を1世代保全する
                        BackupService.Instance.MaybeBackup(_notesFilePath, force: true);
                        return doc.Notes;
                    }
                }

                System.Diagnostics.Debug.WriteLine("[StorageService] notes.json is empty or invalid. Attempting recovery.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StorageService] LoadNotes error: {ex.Message}");
            }

            return RecoverFromBackup();
        }

        /// <summary>
        /// notes.json が破損していた場合に、世代バックアップの新しい順に読み込みを試みて復旧する。
        /// 破損した原本は削除せず backups/ へ退避する。
        /// </summary>
        private List<NoteItem> RecoverFromBackup()
        {
            var quarantinedPath = BackupService.Instance.QuarantineCorruptFile(_notesFilePath);

            foreach (var backupPath in BackupService.Instance.EnumerateBackups())
            {
                try
                {
                    var json = File.ReadAllText(backupPath);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    var doc = JsonSerializer.Deserialize<NotesDocument>(json, _jsonOptions);
                    if (doc?.Notes == null) continue;

                    // 復旧した内容を notes.json として書き戻す
                    File.Copy(backupPath, _notesFilePath, overwrite: true);

                    LastRecovery = new RecoveryInfo
                    {
                        Succeeded = true,
                        RecoveredFromFileName = Path.GetFileName(backupPath),
                        QuarantinedFileName = Path.GetFileName(quarantinedPath),
                        NoteCount = doc.Notes.Count
                    };
                    return doc.Notes;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[StorageService] Recovery from {backupPath} failed: {ex.Message}");
                }
            }

            LastRecovery = new RecoveryInfo
            {
                Succeeded = false,
                QuarantinedFileName = Path.GetFileName(quarantinedPath)
            };
            return new List<NoteItem>();
        }

        public void SaveNotes(List<NoteItem> notes)
        {
            try
            {
                var doc = new NotesDocument
                {
                    Version = 1,
                    Notes = notes
                };

                var json = JsonSerializer.Serialize(doc, _jsonOptions);
                var tempPath = _notesFilePath + ".tmp";

                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _notesFilePath, overwrite: true);

                // 保存に成功した内容のみを世代バックアップの対象にする（間隔条件を満たす場合のみ作成）
                BackupService.Instance.MaybeBackup(_notesFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StorageService] SaveNotes error: {ex.Message}");
            }
        }

        /// <summary>
        /// 間隔条件を無視して世代バックアップを作成する（終了時など）。
        /// </summary>
        public void BackupNow()
        {
            BackupService.Instance.MaybeBackup(_notesFilePath, force: true);
        }

        public string SaveImage(BitmapSource bitmapSource, string noteId)
        {
            try
            {
                var fileName = $"{noteId}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.png";
                var filePath = Path.Combine(_imagesDirectory, fileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                    encoder.Save(fileStream);
                }

                return fileName;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StorageService] SaveImage error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 本文の Markdown に書かれた画像パスを、実ファイルのパスへ解決する。
        ///
        /// 標準の書き方は data ディレクトリからの相対（例: images/xxx.png）。
        /// 本文は手で書き換えられるため、ファイル名だけ（例: xxx.png）や絶対パスも受け付ける。
        /// 実在しない場合は空文字を返す。呼び出し側は代替表示に切り替えること。
        /// </summary>
        public string ResolveImagePath(string pathInMarkdown)
        {
            if (string.IsNullOrWhiteSpace(pathInMarkdown))
            {
                return string.Empty;
            }

            var path = pathInMarkdown.Trim().Replace('/', Path.DirectorySeparatorChar);

            try
            {
                if (Path.IsPathRooted(path))
                {
                    return File.Exists(path) ? path : string.Empty;
                }

                foreach (var baseDirectory in new[] { _dataDirectory, _imagesDirectory })
                {
                    var candidate = Path.GetFullPath(Path.Combine(baseDirectory, path));
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StorageService] ResolveImagePath error: {ex.Message}");
            }

            return string.Empty;
        }

        /// <summary>
        /// 本文へ書き込む画像パスを組み立てる。data ディレクトリからの相対で、区切りは "/" に揃える。
        /// フォルダごと移動しても参照が壊れないようにするため、絶対パスは書かない。
        /// </summary>
        public string BuildMarkdownImagePath(string fileName) => $"{ImagesDirectoryName}/{fileName}";
    }
}
