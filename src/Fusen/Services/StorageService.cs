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

    public class StorageService
    {
        private static StorageService? _instance;
        public static StorageService Instance => _instance ??= new StorageService();

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

            _imagesDirectory = Path.Combine(_dataDirectory, "images");
            _notesFilePath = Path.Combine(_dataDirectory, "notes.json");

            Directory.CreateDirectory(_dataDirectory);
            Directory.CreateDirectory(_imagesDirectory);

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public List<NoteItem> LoadNotes()
        {
            try
            {
                if (!File.Exists(_notesFilePath))
                {
                    return new List<NoteItem>();
                }

                var json = File.ReadAllText(_notesFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<NoteItem>();
                }

                var doc = JsonSerializer.Deserialize<NotesDocument>(json, _jsonOptions);
                return doc?.Notes ?? new List<NoteItem>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StorageService] LoadNotes error: {ex.Message}");
                // バックアップを作成して空リストを返す
                try
                {
                    if (File.Exists(_notesFilePath))
                    {
                        var backupPath = _notesFilePath + $".bak_{DateTime.Now:yyyyMMddHHmmss}";
                        File.Copy(_notesFilePath, backupPath, true);
                    }
                }
                catch { }
                return new List<NoteItem>();
            }
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StorageService] SaveNotes error: {ex.Message}");
            }
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

        public string GetImageFullPath(string fileNameOrPath)
        {
            if (Path.IsPathRooted(fileNameOrPath))
            {
                return fileNameOrPath;
            }
            return Path.Combine(_imagesDirectory, fileNameOrPath);
        }
    }
}
