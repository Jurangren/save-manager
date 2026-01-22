using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Playnite.SDK;
using SaveManager.Models;

namespace SaveManager.Services
{
    /// <summary>
    /// 备份服务 - 处理存档备份和恢复逻辑
    /// </summary>
    public class BackupService
    {
        private readonly ILogger logger;
        private readonly IPlayniteAPI playniteApi;
        private readonly string dataPath;
        private readonly string backupsPath;
        private readonly string configPath;

        private Dictionary<Guid, GameSaveConfig> gameConfigs;
        private Dictionary<Guid, List<SaveBackup>> gameBackups;

        /// <summary>
        /// 获取备份文件夹路径
        /// </summary>
        public string BackupsPath => backupsPath;

        public BackupService(string dataPath, ILogger logger, IPlayniteAPI playniteApi)
        {
            this.logger = logger;
            this.playniteApi = playniteApi;
            this.dataPath = dataPath;
            this.backupsPath = Path.Combine(dataPath, "Backups");
            this.configPath = Path.Combine(dataPath, "config.json");

            // 确保目录存在
            Directory.CreateDirectory(backupsPath);

            // 加载配置
            LoadData();
        }

        /// <summary>
        /// 加载保存的数据
        /// </summary>
        private void LoadData()
        {
            gameConfigs = new Dictionary<Guid, GameSaveConfig>();
            gameBackups = new Dictionary<Guid, List<SaveBackup>>();

            try
            {
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var data = Playnite.SDK.Data.Serialization.FromJson<SaveDataModel>(json);
                    if (data != null)
                    {
                        gameConfigs = data.GameConfigs ?? new Dictionary<Guid, GameSaveConfig>();
                        gameBackups = data.GameBackups ?? new Dictionary<Guid, List<SaveBackup>>();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to load save manager data");
            }
        }

        /// <summary>
        /// 保存数据
        /// </summary>
        private void SaveData()
        {
            try
            {
                var data = new SaveDataModel
                {
                    GameConfigs = gameConfigs,
                    GameBackups = gameBackups
                };
                var json = Playnite.SDK.Data.Serialization.ToJson(data, true);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to save save manager data");
            }
        }

        /// <summary>
        /// 获取游戏的存档配置
        /// </summary>
        public GameSaveConfig GetGameConfig(Guid gameId)
        {
            if (gameConfigs.TryGetValue(gameId, out var config))
            {
                return config;
            }
            return null;
        }

        /// <summary>
        /// 保存游戏的存档配置
        /// </summary>
        public void SaveGameConfig(GameSaveConfig config)
        {
            config.UpdatedAt = DateTime.Now;
            gameConfigs[config.GameId] = config;
            SaveData();
        }

        /// <summary>
        /// 获取游戏的所有备份
        /// </summary>
        public List<SaveBackup> GetBackups(Guid gameId)
        {
            if (gameBackups.TryGetValue(gameId, out var backups))
            {
                return backups.OrderByDescending(b => b.CreatedAt).ToList();
            }
            return new List<SaveBackup>();
        }

        /// <summary>
        /// 创建备份
        /// </summary>
        public SaveBackup CreateBackup(Guid gameId, string gameName, string description = null)
        {
            var config = GetGameConfig(gameId);
            if (config == null || config.SavePaths.Count == 0)
            {
                throw new InvalidOperationException("No save paths configured for this game.");
            }

            var game = playniteApi.Database.Games.Get(gameId);
            var installDir = game?.InstallDirectory;

            // 验证解析后的路径存在
            foreach (var savePath in config.SavePaths)
            {
                var absolutePath = PathHelper.ResolvePath(savePath.Path, installDir);
                
                if (savePath.IsDirectory && !Directory.Exists(absolutePath))
                {
                    throw new FileNotFoundException($"Save directory not found: {absolutePath}");
                }
                if (!savePath.IsDirectory && !File.Exists(absolutePath))
                {
                    throw new FileNotFoundException($"Save file not found: {absolutePath}");
                }
            }

            // 创建游戏专属备份文件夹
            var gameBackupPath = GetGameBackupDirectory(gameId, gameName);
            Directory.CreateDirectory(gameBackupPath);

            // 创建备份
            var backup = new SaveBackup
            {
                GameId = gameId,
                Name = $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}",
                Description = description ?? "",
                CreatedAt = DateTime.Now
            };

            var backupFileName = $"{backup.Name}.zip";
            backup.BackupFilePath = Path.Combine(gameBackupPath, backupFileName);

            // 创建ZIP文件
            CreateZipBackup(config.SavePaths, backup.BackupFilePath, installDir);

            // 获取文件大小
            var fileInfo = new FileInfo(backup.BackupFilePath);
            backup.FileSize = fileInfo.Length;

            // 保存备份记录
            if (!gameBackups.ContainsKey(gameId))
            {
                gameBackups[gameId] = new List<SaveBackup>();
            }
            gameBackups[gameId].Add(backup);
            SaveData();

            logger.Info($"Created backup for game {gameName}: {backup.BackupFilePath}");
            return backup;
        }

        /// <summary>
        /// 创建ZIP备份文件
        /// </summary>
        private void CreateZipBackup(List<SavePath> savePaths, string zipPath, string installDir)
        {
            // 如果文件已存在则删除
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var savePath in savePaths)
                {
                    var absolutePath = PathHelper.ResolvePath(savePath.Path, installDir);

                    if (savePath.IsDirectory)
                    {
                        AddDirectoryToZip(zip, absolutePath, savePath.DisplayName);
                    }
                    else
                    {
                        // 添加单个文件
                        zip.CreateEntryFromFile(absolutePath, savePath.DisplayName);
                    }
                }

                // 添加路径映射文件（用于还原时确定原始路径）
                var mappingEntry = zip.CreateEntry("__save_paths__.json");
                using (var writer = new StreamWriter(mappingEntry.Open()))
                {
                    var mapping = savePaths.Select(p => new
                    {
                        OriginalPath = p.Path,
                        IsDirectory = p.IsDirectory,
                        EntryName = p.DisplayName
                    });
                    writer.Write(Playnite.SDK.Data.Serialization.ToJson(mapping, true));
                }
            }
        }

        /// <summary>
        /// 递归添加目录到ZIP
        /// </summary>
        private void AddDirectoryToZip(ZipArchive zip, string sourceDir, string entryPrefix)
        {
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var entryName = Path.Combine(entryPrefix, Path.GetFileName(file));
                zip.CreateEntryFromFile(file, entryName.Replace("\\", "/"));
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                var newPrefix = Path.Combine(entryPrefix, dirName);
                AddDirectoryToZip(zip, dir, newPrefix);
            }
        }

        /// <summary>
        /// 还原备份
        /// </summary>
        public void RestoreBackup(SaveBackup backup)
        {
            if (!File.Exists(backup.BackupFilePath))
            {
                throw new FileNotFoundException($"Backup file not found: {backup.BackupFilePath}");
            }

            var game = playniteApi.Database.Games.Get(backup.GameId);
            var installDir = game?.InstallDirectory;

            using (var zip = ZipFile.OpenRead(backup.BackupFilePath))
            {
                // 读取路径映射
                var mappingEntry = zip.GetEntry("__save_paths__.json");
                if (mappingEntry == null)
                {
                    throw new InvalidOperationException("Invalid backup file: missing path mapping.");
                }

                List<PathMapping> mappings;
                using (var reader = new StreamReader(mappingEntry.Open()))
                {
                    var json = reader.ReadToEnd();
                    mappings = Playnite.SDK.Data.Serialization.FromJson<List<PathMapping>>(json);
                }

                // 还原每个路径
                foreach (var mapping in mappings)
                {
                    // 检查是否依赖游戏目录但无法获取
                    if (mapping.OriginalPath.Contains(PathHelper.GameDirVariable) && string.IsNullOrEmpty(installDir))
                    {
                        throw new InvalidOperationException($"无法还原备份：配置路径包含 '{{GameDir}}'，但无法获取该游戏的安装目录。\n请确保游戏已安装并配置了安装路径。");
                    }

                    var originalPath = PathHelper.ResolvePath(mapping.OriginalPath, installDir);
                    
                    // 双重检查：确保解析后的路径不再包含变量
                    if (originalPath.Contains(PathHelper.GameDirVariable))
                    {
                         throw new InvalidOperationException($"路径解析失败：无法解析 '{{GameDir}}' 变量。\n路径: {originalPath}");
                    }

                    if (mapping.IsDirectory)
                    {
                        // 清空目标目录（如果存在）
                        if (Directory.Exists(originalPath))
                        {
                            // 备份当前存档（可选，这里直接覆盖）
                            // 删除目录内容
                            foreach (var file in Directory.GetFiles(originalPath, "*", SearchOption.AllDirectories))
                            {
                                File.Delete(file);
                            }
                        }
                        else
                        {
                            Directory.CreateDirectory(mapping.OriginalPath);
                        }

                        // 提取匹配的条目
                        foreach (var entry in zip.Entries)
                        {
                            if (entry.FullName.StartsWith(mapping.EntryName + "/") && 
                                entry.FullName != "__save_paths__.json" &&
                                !string.IsNullOrEmpty(entry.Name))
                            {
                                var relativePath = entry.FullName.Substring(mapping.EntryName.Length + 1);
                                var targetPath = Path.Combine(originalPath, relativePath.Replace("/", "\\"));
                                
                                // 确保目录存在
                                var targetDir = Path.GetDirectoryName(targetPath);
                                if (!Directory.Exists(targetDir))
                                {
                                    Directory.CreateDirectory(targetDir);
                                }

                                entry.ExtractToFile(targetPath, true);
                            }
                        }
                    }
                    else
                    {
                        // 还原单个文件
                        var entry = zip.GetEntry(mapping.EntryName);
                        if (entry != null)
                        {
                            var targetDir = Path.GetDirectoryName(originalPath);
                            if (!Directory.Exists(targetDir))
                            {
                                Directory.CreateDirectory(targetDir);
                            }
                            entry.ExtractToFile(originalPath, true);
                        }
                    }
                }
            }

            logger.Info($"Restored backup: {backup.BackupFilePath}");
        }

        /// <summary>
        /// 导入外部备份文件
        /// </summary>
        public SaveBackup ImportBackup(Guid gameId, string gameName, string sourceFilePath)
        {
            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("Source file not found", sourceFilePath);
            }

            // 验证 ZIP 文件有效性 (包含 __save_paths__.json)
            try 
            {
                using (var zip = ZipFile.OpenRead(sourceFilePath))
                {
                    if (zip.GetEntry("__save_paths__.json") == null)
                    {
                         throw new InvalidOperationException("Invalid backup file: missing path definition (__save_paths__.json).");
                    }
                }
            } 
            catch (Exception ex)
            {
                 throw new InvalidOperationException($"Invalid ZIP file: {ex.Message}", ex);
            }

            // 准备目标路径
            var gameBackupPath = GetGameBackupDirectory(gameId, gameName);
            Directory.CreateDirectory(gameBackupPath);

            // 生成新文件名
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 4);
            var backupName = $"Imported_{timestamp}_{uniqueId}";
            var fileName = $"{backupName}.zip";
            var destPath = Path.Combine(gameBackupPath, fileName);

            // 复制文件
            File.Copy(sourceFilePath, destPath);

            // 创建记录
            var fileInfo = new FileInfo(destPath);
            
            var backup = new SaveBackup
            {
                GameId = gameId,
                Name = backupName,
                Description = "Imported Backup",
                BackupFilePath = destPath,
                CreatedAt = DateTime.Now,
                FileSize = fileInfo.Length
            };

            // 保存记录
            if (!gameBackups.ContainsKey(gameId))
            {
                gameBackups[gameId] = new List<SaveBackup>();
            }
            gameBackups[gameId].Add(backup);
            SaveData();

            logger.Info($"Imported backup for game {gameName}: {destPath}");
            return backup;
        }

        /// <summary>
        /// 删除备份
        /// </summary>
        public void DeleteBackup(SaveBackup backup)
        {
            if (File.Exists(backup.BackupFilePath))
            {
                File.Delete(backup.BackupFilePath);
            }

            if (gameBackups.TryGetValue(backup.GameId, out var backups))
            {
                backups.RemoveAll(b => b.Id == backup.Id);
                
                // 如果该游戏没有备份了，清理相关数据和目录
                if (backups.Count == 0)
                {
                    try
                    {
                        var backupDir = Path.GetDirectoryName(backup.BackupFilePath);
                        if (Directory.Exists(backupDir) && !Directory.EnumerateFileSystemEntries(backupDir).Any())
                        {
                            Directory.Delete(backupDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "Failed to delete empty backup directory");
                    }

                    gameBackups.Remove(backup.GameId);
                }
                
                SaveData();
            }

            logger.Info($"Deleted backup: {backup.BackupFilePath}");
        }

        /// <summary>
        /// 更新备份描述
        /// </summary>
        public void UpdateBackupDescription(SaveBackup backup, string description)
        {
            if (gameBackups.TryGetValue(backup.GameId, out var backups))
            {
                var existingBackup = backups.FirstOrDefault(b => b.Id == backup.Id);
                if (existingBackup != null)
                {
                    existingBackup.Description = description;
                    SaveData();
                }
            }
        }

        /// <summary>
        /// 获取游戏的备份目录
        /// </summary>
        public string GetGameBackupDirectory(Guid gameId, string gameName)
        {
            return Path.Combine(backupsPath, SanitizeFileName(gameName) + "_" + gameId.ToString("N").Substring(0, 8));
        }

        /// <summary>
        /// 清理文件名，移除非法字符
        /// </summary>
        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var result = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
            return string.IsNullOrWhiteSpace(result) ? "game" : result;
        }

        /// <summary>
        /// 存档数据
        /// </summary>
        private class SaveDataModel
        {
            public Dictionary<Guid, GameSaveConfig> GameConfigs { get; set; }
            public Dictionary<Guid, List<SaveBackup>> GameBackups { get; set; }
        }

        /// <summary>
        /// 路径映射
        /// </summary>
        private class PathMapping
        {
            public string OriginalPath { get; set; }
            public bool IsDirectory { get; set; }
            public string EntryName { get; set; }
        }
    }
}
