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
    /// 支持多设备：使用 ConfigId 作为配置主键，GameIds 列表关联多台设备的游戏
    /// </summary>
    public class BackupService
    {
        private readonly ILogger logger;
        private readonly IPlayniteAPI playniteApi;
        private readonly string dataPath;
        private readonly string backupsPath;
        private readonly string configPath;
        private readonly Func<bool> getRealtimeSyncEnabled;

        // 使用 ConfigId 作为主键存储配置
        private Dictionary<Guid, GameSaveConfig> gameConfigs;
        // 使用 ConfigId 作为主键存储备份
        private Dictionary<Guid, List<SaveBackup>> gameBackups;
        // GameId -> ConfigId 的快速查找索引
        private Dictionary<Guid, Guid> gameIdToConfigId;

        /// <summary>
        /// 获取备份文件夹路径
        /// </summary>
        public string BackupsPath => backupsPath;

        /// <summary>
        /// 数据版本号（用于迁移）
        /// </summary>
        private const int CurrentDataVersion = 2;

        public BackupService(string dataPath, ILogger logger, IPlayniteAPI playniteApi, Func<bool> getRealtimeSyncEnabled = null)
        {
            this.logger = logger;
            this.playniteApi = playniteApi;
            this.dataPath = dataPath;
            this.backupsPath = Path.Combine(dataPath, "Backups");
            this.configPath = Path.Combine(dataPath, "config.json");
            this.getRealtimeSyncEnabled = getRealtimeSyncEnabled;

            // 确保目录存在
            Directory.CreateDirectory(backupsPath);

            // 加载配置
            LoadData();
        }

        /// <summary>
        /// 加载保存的数据（支持旧版本数据自动迁移）
        /// </summary>
        private void LoadData()
        {
            gameConfigs = new Dictionary<Guid, GameSaveConfig>();
            gameBackups = new Dictionary<Guid, List<SaveBackup>>();
            gameIdToConfigId = new Dictionary<Guid, Guid>();

            try
            {
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var data = Playnite.SDK.Data.Serialization.FromJson<SaveDataModelV2>(json);
                    
                    if (data != null)
                    {
                        // 检查是否需要迁移
                        if (data.Version < CurrentDataVersion || data.Version == 0)
                        {
                            MigrateFromV1(json);
                        }
                        else
                        {
                            gameConfigs = data.GameConfigs ?? new Dictionary<Guid, GameSaveConfig>();
                            gameBackups = data.GameBackups ?? new Dictionary<Guid, List<SaveBackup>>();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to load save manager data");
            }

            // 重建 GameId -> ConfigId 索引
            RebuildGameIdIndex();
        }

        /// <summary>
        /// 从 V1 版本迁移数据
        /// V1: 使用 GameId 作为主键
        /// V2: 使用 ConfigId 作为主键
        /// </summary>
        private void MigrateFromV1(string json)
        {
            logger.Info("Migrating data from V1 to V2 format...");

            try
            {
                var oldData = Playnite.SDK.Data.Serialization.FromJson<SaveDataModelV1>(json);
                if (oldData == null) return;

                var oldConfigs = oldData.GameConfigs ?? new Dictionary<Guid, GameSaveConfig>();
                var oldBackups = oldData.GameBackups ?? new Dictionary<Guid, List<SaveBackup>>();

                foreach (var kvp in oldConfigs)
                {
                    var oldGameId = kvp.Key;
                    var config = kvp.Value;

                    // 确保 ConfigId 存在
                    if (config.ConfigId == Guid.Empty)
                    {
                        config.ConfigId = Guid.NewGuid();
                    }

                    // 确保 GameIds 列表包含原来的 GameId
                    if (!config.GameIds.Contains(oldGameId))
                    {
                        config.GameIds.Insert(0, oldGameId);
                    }

                    // 使用 ConfigId 作为新的主键
                    gameConfigs[config.ConfigId] = config;

                    // 迁移备份数据
                    if (oldBackups.TryGetValue(oldGameId, out var backups))
                    {
                        foreach (var backup in backups)
                        {
                            // 设置 ConfigId
                            if (backup.ConfigId == Guid.Empty)
                            {
                                backup.ConfigId = config.ConfigId;
                            }
                        }
                        gameBackups[config.ConfigId] = backups;
                    }
                }

                // 保存迁移后的数据
                SaveData();
                logger.Info($"Migration completed. Migrated {gameConfigs.Count} configs and {gameBackups.Values.Sum(b => b.Count)} backups.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to migrate data from V1");
            }
        }

        /// <summary>
        /// 重建 GameId -> ConfigId 索引
        /// </summary>
        private void RebuildGameIdIndex()
        {
            gameIdToConfigId.Clear();
            foreach (var config in gameConfigs.Values)
            {
                foreach (var gameId in config.GameIds)
                {
                    gameIdToConfigId[gameId] = config.ConfigId;
                }
            }
        }

        /// <summary>
        /// 获取备份文件的完整路径
        /// </summary>
        public string GetFullBackupPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Path.IsPathRooted(path)) return path; // 已经是绝对路径
            return Path.Combine(dataPath, path);
        }

        /// <summary>
        /// 获取相对于数据目录的路径
        /// </summary>
        public string GetRelativeBackupPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            
            string baseDir = dataPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            if (fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                string relative = fullPath.Substring(baseDir.Length);
                if (relative.StartsWith(Path.DirectorySeparatorChar.ToString()) || relative.StartsWith(Path.AltDirectorySeparatorChar.ToString()))
                {
                    relative = relative.Substring(1);
                }
                return relative;
            }
            return fullPath;
        }

        /// <summary>
        /// 计算备份文件内容的 CRC32 校验值
        /// 排除元数据文件：backup_info.json 和 __save_paths__.json
        /// </summary>
        private string ComputeCrc32(string filePath)
        {
            const uint polynomial = 0xEDB88320;
            uint[] table = new uint[256];

            // 构建 CRC32 查找表
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ polynomial;
                    else
                        crc >>= 1;
                }
                table[i] = crc;
            }

            // 计算 ZIP 内容的 CRC32
            using (var archive = ZipFile.OpenRead(filePath))
            {
                // 筛选并排序 Entry，确保确定性
                var entries = archive.Entries
                    .Where(e => !e.FullName.EndsWith("/") && // 忽略文件夹 Entry
                                !e.Name.Equals("backup_info.json", StringComparison.OrdinalIgnoreCase) && 
                                !e.Name.Equals("__save_paths__.json", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase);

                uint crc = 0xFFFFFFFF;
                byte[] buffer = new byte[8192];

                foreach (var entry in entries)
                {
                    using (var stream = entry.Open())
                    {
                        int bytesRead;
                        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            for (int k = 0; k < bytesRead; k++)
                            {
                                byte index = (byte)(((crc) & 0xFF) ^ buffer[k]);
                                crc = (crc >> 8) ^ table[index];
                            }
                        }
                    }
                }

                return (~crc).ToString("X8");
            }
        }

        /// <summary>
        /// 保存数据
        /// </summary>
        private void SaveData()
        {
            try
            {
                var data = new SaveDataModelV2
                {
                    Version = CurrentDataVersion,
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

        #region 配置相关方法

        /// <summary>
        /// 通过 GameId 获取游戏的存档配置
        /// </summary>
        public GameSaveConfig GetGameConfig(Guid gameId)
        {
            if (gameIdToConfigId.TryGetValue(gameId, out var configId))
            {
                if (gameConfigs.TryGetValue(configId, out var config))
                {
                    return config;
                }
            }
            return null;
        }

        /// <summary>
        /// 通过 ConfigId 获取配置
        /// </summary>
        public GameSaveConfig GetConfigByConfigId(Guid configId)
        {
            if (gameConfigs.TryGetValue(configId, out var config))
            {
                return config;
            }
            return null;
        }

        /// <summary>
        /// 获取所有游戏配置
        /// </summary>
        public List<GameSaveConfig> GetAllGameConfigs()
        {
            return gameConfigs.Values.ToList();
        }

        /// <summary>
        /// 保存游戏的存档配置
        /// </summary>
        public void SaveGameConfig(GameSaveConfig config)
        {
            config.UpdatedAt = DateTime.Now;
            
            // 确保 ConfigId 存在
            if (config.ConfigId == Guid.Empty)
            {
                config.ConfigId = Guid.NewGuid();
            }

            gameConfigs[config.ConfigId] = config;
            
            // 更新索引
            foreach (var gameId in config.GameIds)
            {
                gameIdToConfigId[gameId] = config.ConfigId;
            }
            
            SaveData();
        }

        /// <summary>
        /// 保存所有配置（批量保存，避免多次写入文件）
        /// </summary>
        public void SaveAllConfigs()
        {
            RebuildGameIdIndex();
            SaveData();
        }

        /// <summary>
        /// 删除游戏配置及所有备份数据
        /// </summary>
        public void DeleteGameConfig(Guid configId)
        {
            // 1. 删除备份文件及目录
            if (gameBackups.TryGetValue(configId, out var backups))
            {
                var backupsToDelete = backups.ToList();
                HashSet<string> directoriesToDelete = new HashSet<string>();
                
                foreach (var backup in backupsToDelete)
                {
                    try
                    {
                        var fullPath = GetFullBackupPath(backup.BackupFilePath);
                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                        }
                        
                        var dir = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            directoriesToDelete.Add(dir);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Failed to delete backup file: {backup.BackupFilePath}");
                    }
                }
                
                // 删除相关的备份目录
                foreach (var dir in directoriesToDelete)
                {
                    try
                    {
                        if (Directory.Exists(dir))
                        {
                             Directory.Delete(dir, true);
                        }
                    }
                    catch (Exception ex)
                    {
                         logger.Error(ex, $"Failed to delete backup directory: {dir}");
                    }
                }
                
                gameBackups.Remove(configId);
            }

            // 2. 删除配置
            // 使用更鲁棒的方式查找：同时匹配 Key 和 Value.ConfigId
            // 防止因为某种原因导致的 Key 不一致
            var keysToRemove = gameConfigs.Where(kv => kv.Key == configId || kv.Value.ConfigId == configId)
                                        .Select(kv => kv.Key)
                                        .ToList();
            
            foreach (var key in keysToRemove)
            {
                gameConfigs.Remove(key);
            }

            // 3. 重建索引并保存
            RebuildGameIdIndex();
            SaveData();
        }

        /// <summary>
        /// 为游戏创建新配置（如果不存在）
        /// </summary>
        public GameSaveConfig GetOrCreateGameConfig(Guid gameId, string gameName)
        {
            var config = GetGameConfig(gameId);
            if (config == null)
            {
                config = new GameSaveConfig
                {
                    ConfigId = Guid.NewGuid(),
                    GameName = gameName
                };
                config.GameIds.Add(gameId);
                SaveGameConfig(config);
            }
            return config;
        }

        #endregion

        #region 备份相关方法

        /// <summary>
        /// 通过 GameId 获取游戏的所有备份
        /// </summary>
        public List<SaveBackup> GetBackups(Guid gameId)
        {
            var config = GetGameConfig(gameId);
            if (config != null)
            {
                return GetBackupsByConfigId(config.ConfigId);
            }
            return new List<SaveBackup>();
        }

        /// <summary>
        /// 通过 ConfigId 获取备份
        /// </summary>
        public List<SaveBackup> GetBackupsByConfigId(Guid configId)
        {
            if (gameBackups.TryGetValue(configId, out var backups))
            {
                return backups.OrderByDescending(b => b.CreatedAt).ToList();
            }
            return new List<SaveBackup>();
        }

        /// <summary>
        /// 创建备份
        /// </summary>
        /// <param name="gameId">游戏ID</param>
        /// <param name="gameName">游戏名称</param>
        /// <param name="description">备注</param>
        /// <param name="isAutoBackup">是否为自动备份（自动备份受数量限制，手动备份不受）</param>
        public SaveBackup CreateBackup(Guid gameId, string gameName, string description = null, bool isAutoBackup = false)
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
            var gameBackupPath = GetGameBackupDirectory(config.ConfigId, gameName);
            Directory.CreateDirectory(gameBackupPath);

            // 创建备份
            var backup = new SaveBackup
            {
                ConfigId = config.ConfigId,
                GameId = gameId,
                Name = $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}",
                Description = description ?? "",
                CreatedAt = DateTime.Now,
                IsAutoBackup = isAutoBackup
            };

            var backupFileName = $"{backup.Name}.zip";
            var fullBackupPath = Path.Combine(gameBackupPath, backupFileName);
            
            // 存储相对路径
            backup.BackupFilePath = GetRelativeBackupPath(fullBackupPath);

            // 创建ZIP文件（包含备份信息）
            CreateZipBackup(config.SavePaths, fullBackupPath, installDir, gameName, description ?? "");

            // 获取文件大小
            var fileInfo = new FileInfo(fullBackupPath);
            backup.FileSize = fileInfo.Length;

            // 计算备份文件的 CRC32 校验值
            backup.CRC = ComputeCrc32(fullBackupPath);

            // 如果启用了实时同步，继承 Latest.zip 的 VersionHistory
            bool isRealtimeSyncEnabled = getRealtimeSyncEnabled?.Invoke() ?? false;
            if (isRealtimeSyncEnabled)
            {
                // 查找 Latest.zip 备份
                SaveBackup latestBackup = null;
                if (gameBackups.ContainsKey(config.ConfigId))
                {
                    latestBackup = gameBackups[config.ConfigId]
                        .FirstOrDefault(b => b.Name == "Latest");
                }

                // 继承 Latest.zip 的 VersionHistory
                if (latestBackup != null && latestBackup.VersionHistory != null && latestBackup.VersionHistory.Count > 0)
                {
                    backup.VersionHistory.AddRange(latestBackup.VersionHistory);
                }
            }

            // 添加当前备份的 CRC 到历史（去重：如果与最后一个相同则不添加）
            if (backup.VersionHistory.Count == 0 || backup.VersionHistory.Last() != backup.CRC)
            {
                backup.VersionHistory.Add(backup.CRC);
            }

            // 保存备份记录
            if (!gameBackups.ContainsKey(config.ConfigId))
            {
                gameBackups[config.ConfigId] = new List<SaveBackup>();
            }
            gameBackups[config.ConfigId].Add(backup);
            SaveData();

            // 如果启用了实时同步，将此备份复制为 Latest.zip
            if (isRealtimeSyncEnabled)
            {
                try
                {
                    CopyBackupToLatest(backup, config, gameName, fullBackupPath);
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"Failed to copy backup to Latest.zip for game {gameName}");
                    playniteApi.Dialogs.ShowErrorMessage(
                        $"Failed to update Latest.zip: {ex.Message}", 
                        "Real-time Sync Error");
                }
            }

            logger.Info($"Created {(isAutoBackup ? "auto" : "manual")} backup for game {gameName}: {fullBackupPath} (CRC: {backup.CRC}, History: {backup.VersionHistory.Count})");
            return backup;
        }

        /// <summary>
        /// 将备份复制为 Latest.zip（用于实时同步）
        /// </summary>
        private void CopyBackupToLatest(SaveBackup sourceBackup, GameSaveConfig config, string gameName, string sourceFilePath)
        {
            var gameBackupPath = GetGameBackupDirectory(config.ConfigId, gameName);
            var latestFileName = "Latest.zip";
            var latestFullPath = Path.Combine(gameBackupPath, latestFileName);

            // 复制文件
            File.Copy(sourceFilePath, latestFullPath, overwrite: true);

            // 查找并移除旧的 Latest 记录
            if (gameBackups.ContainsKey(config.ConfigId))
            {
                var oldLatest = gameBackups[config.ConfigId]
                    .FirstOrDefault(b => b.Name == "Latest");
                
                if (oldLatest != null)
                {
                    gameBackups[config.ConfigId].Remove(oldLatest);
                }
            }

            // 创建新的 Latest 记录（使用相同的 VersionHistory 和 CRC）
            var latestBackup = new SaveBackup
            {
                ConfigId = config.ConfigId,
                GameId = sourceBackup.GameId,
                Name = "Latest",
                Description = ResourceProvider.GetString("LOCSaveManagerRealtimeSyncDescription"),
                CreatedAt = sourceBackup.CreatedAt,
                BackupFilePath = GetRelativeBackupPath(latestFullPath),
                FileSize = sourceBackup.FileSize,
                CRC = sourceBackup.CRC,
                IsAutoBackup = false
            };

            // 复制 VersionHistory
            latestBackup.VersionHistory.AddRange(sourceBackup.VersionHistory);

            // 添加 Latest 记录
            if (!gameBackups.ContainsKey(config.ConfigId))
            {
                gameBackups[config.ConfigId] = new List<SaveBackup>();
            }
            gameBackups[config.ConfigId].Add(latestBackup);
            SaveData();

            logger.Info($"Copied backup to Latest.zip for game {gameName} (CRC: {latestBackup.CRC}, History: {latestBackup.VersionHistory.Count})");
        }

        /// <summary>
        /// 创建实时同步快照 (Latest.zip)
        /// </summary>
        /// <param name="gameId">游戏ID</param>
        /// <param name="gameName">游戏名称</param>
        /// <param name="baseVersionHistory">基础版本历史（可选）。如果提供，将继承此历史；否则从现有的 Latest.zip 继承。</param>
        public SaveBackup CreateRealtimeSyncSnapshot(Guid gameId, string gameName, List<string> baseVersionHistory = null)
        {
            var config = GetGameConfig(gameId);
            if (config == null || config.SavePaths.Count == 0)
            {
                throw new InvalidOperationException("No save paths configured for this game.");
            }

            var game = playniteApi.Database.Games.Get(gameId);
            var installDir = game?.InstallDirectory;

            // 验证路径存在
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
            var gameBackupPath = GetGameBackupDirectory(config.ConfigId, gameName);
            Directory.CreateDirectory(gameBackupPath);

            // 确定 VersionHistory 的继承来源
            List<string> inheritedHistory = new List<string>();
            SaveBackup previousLatest = null;
            
            // 查找现有的 Latest 记录（用于稍后替换）
            if (gameBackups.ContainsKey(config.ConfigId))
            {
                previousLatest = gameBackups[config.ConfigId]
                    .FirstOrDefault(b => b.Name == "Latest");
            }

            if (baseVersionHistory != null)
            {
                // 如果指定了基础历史，直接使用
                inheritedHistory.AddRange(baseVersionHistory);
            }
            else if (previousLatest != null && previousLatest.VersionHistory != null)
            {
                // 否则，从现有的 Latest.zip 继承
                inheritedHistory.AddRange(previousLatest.VersionHistory);
            }

            // 创建新的实时同步备份
            var backup = new SaveBackup
            {
                ConfigId = config.ConfigId,
                GameId = gameId,
                Name = "Latest",
                Description = ResourceProvider.GetString("LOCSaveManagerRealtimeSyncDescription"),
                CreatedAt = DateTime.Now,
                IsAutoBackup = false
            };

            var backupFileName = "Latest.zip";
            var fullBackupPath = Path.Combine(gameBackupPath, backupFileName);
            
            // 存储相对路径
            backup.BackupFilePath = GetRelativeBackupPath(fullBackupPath);

            // 创建ZIP文件
            CreateZipBackup(config.SavePaths, fullBackupPath, installDir, gameName, backup.Description);

            // 获取文件大小
            var fileInfo = new FileInfo(fullBackupPath);
            backup.FileSize = fileInfo.Length;

            // 计算 CRC32
            backup.CRC = ComputeCrc32(fullBackupPath);

            // 继承之前的 VersionHistory 并追加当前 CRC（去重：如果与最后一个相同则不添加）
            backup.VersionHistory.AddRange(inheritedHistory);
            if (backup.VersionHistory.Count == 0 || backup.VersionHistory.Last() != backup.CRC)
            {
                backup.VersionHistory.Add(backup.CRC);
            }

            // 如果之前存在 Latest，则替换；否则新增
            if (previousLatest != null)
            {
                gameBackups[config.ConfigId].Remove(previousLatest);
            }
            
            if (!gameBackups.ContainsKey(config.ConfigId))
            {
                gameBackups[config.ConfigId] = new List<SaveBackup>();
            }
            gameBackups[config.ConfigId].Add(backup);
            SaveData();

            logger.Info($"Created realtime sync snapshot for game {gameName}: {fullBackupPath} (CRC: {backup.CRC}, History: {backup.VersionHistory.Count} versions)");
            return backup;
        }

        /// <summary>
        /// 创建ZIP备份文件
        /// </summary>
        private void CreateZipBackup(List<SavePath> savePaths, string zipPath, string installDir, string gameName, string description)
        {
            // 如果文件已存在则删除
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                // ------------- 1. 写入备份元数据 (backup_info.json) -------------
                var infoEntry = zip.CreateEntry("backup_info.json");
                using (var writer = new StreamWriter(infoEntry.Open()))
                {
                    var info = new BackupInfo
                    {
                        Description = description,
                        CreatedAt = DateTime.Now,
                        GameName = gameName,
                        Version = 1
                    };
                    writer.Write(Playnite.SDK.Data.Serialization.ToJson(info, true));
                }
                // -----------------------------------------------------------

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
        /// <param name="backup">备份对象</param>
        /// <param name="excludePaths">还原排除项（可选），这些路径将保持当前状态不被覆盖</param>
        public void RestoreBackup(SaveBackup backup, List<SavePath> excludePaths = null)
        {
            var fullPath = GetFullBackupPath(backup.BackupFilePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Backup file not found: {fullPath}");
            }

            // 尝试获取游戏安装目录 - 首先尝试通过 GameId，然后通过 ConfigId 查找任意匹配的游戏
            string installDir = null;
            
            // 先尝试用备份记录的 GameId
            var game = playniteApi.Database.Games.Get(backup.GameId);
            if (game != null)
            {
                installDir = game.InstallDirectory;
            }
            else
            {
                // 如果找不到，尝试通过 ConfigId 查找配置中的任意游戏
                var config = GetConfigByConfigId(backup.ConfigId);
                if (config != null)
                {
                    foreach (var gameId in config.GameIds)
                    {
                        game = playniteApi.Database.Games.Get(gameId);
                        if (game != null)
                        {
                            installDir = game.InstallDirectory;
                            break;
                        }
                    }
                }
            }

            // 预处理排除路径列表（解析变量）
            var resolvedExcludePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excludePaths != null)
            {
                foreach (var excludePath in excludePaths)
                {
                    var resolvedPath = PathHelper.ResolvePath(excludePath.Path, installDir);
                    resolvedExcludePaths.Add(resolvedPath);
                }
            }

            using (var zip = ZipFile.OpenRead(fullPath))
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
                        // 清空目标目录（如果存在），但跳过排除项
                        if (Directory.Exists(originalPath))
                        {
                            // 删除目录内容，但排除指定的文件和文件夹
                            foreach (var file in Directory.GetFiles(originalPath, "*", SearchOption.AllDirectories))
                            {
                                // 检查是否在排除列表中
                                if (!ShouldExcludePath(file, resolvedExcludePaths))
                                {
                                    File.Delete(file);
                                }
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
                                
                                // 检查目标路径是否在排除列表中
                                if (ShouldExcludePath(targetPath, resolvedExcludePaths))
                                {
                                    logger.Info($"Skipping excluded path during restore: {targetPath}");
                                    continue;
                                }

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
                        // 还原单个文件，但检查排除列表
                        if (ShouldExcludePath(originalPath, resolvedExcludePaths))
                        {
                            logger.Info($"Skipping excluded file during restore: {originalPath}");
                            continue;
                        }

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
            
            // 如果启用了实时同步，将还原的备份复制为 Latest.zip
            // 如果启用了实时同步，还原后立即创建一个新的实时快照作为 Latest.zip
            if (getRealtimeSyncEnabled?.Invoke() ?? false)
            {
                try
                {
                    // 获取游戏名称
                    var gameName = playniteApi.Database.Games.Get(backup.GameId)?.Name ?? "Unknown Game";

                    // 重新创建快照（这将基于当前磁盘文件生成 Latest.zip）
                    // 并且基于被还原备份的历史记录
                    CreateRealtimeSyncSnapshot(backup.GameId, gameName, backup.VersionHistory);
                    logger.Info($"Re-created Latest.zip after restore for game {gameName}");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Failed to update Latest.zip after restore");
                    // 弹窗提示用户文件被占用或其他错误
                    playniteApi.Dialogs.ShowErrorMessage(
                        $"Failed to update Latest.zip: {ex.Message}", 
                        "Real-time Sync Error");
                }
            }
        }

        /// <summary>
        /// 检查路径是否应该被排除
        /// </summary>
        /// <param name="path">要检查的路径</param>
        /// <param name="excludePaths">排除路径集合</param>
        /// <returns>如果应该排除返回 true</returns>
        private bool ShouldExcludePath(string path, HashSet<string> excludePaths)
        {
            if (excludePaths == null || excludePaths.Count == 0)
                return false;

            var normalizedPath = Path.GetFullPath(path);

            foreach (var excludePath in excludePaths)
            {
                var normalizedExclude = Path.GetFullPath(excludePath);

                // 精确匹配
                if (normalizedPath.Equals(normalizedExclude, StringComparison.OrdinalIgnoreCase))
                    return true;

                // 检查是否在排除的文件夹下
                if (normalizedPath.StartsWith(normalizedExclude.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
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

            // 获取或创建配置
            var config = GetOrCreateGameConfig(gameId, gameName);

            string importedDescription = "Imported Backup";
            DateTime importedCreatedAt = DateTime.Now;

            // 验证 ZIP 文件有效性并读取元数据
            try 
            {
                using (var zip = ZipFile.OpenRead(sourceFilePath))
                {
                    if (zip.GetEntry("__save_paths__.json") == null)
                    {
                         throw new InvalidOperationException("Invalid backup file: missing path definition (__save_paths__.json).");
                    }

                    // 尝试读取 backup_info.json
                    var infoEntry = zip.GetEntry("backup_info.json");
                    if (infoEntry != null)
                    {
                        try
                        {
                            using (var reader = new StreamReader(infoEntry.Open()))
                            {
                                var info = Playnite.SDK.Data.Serialization.FromJson<BackupInfo>(reader.ReadToEnd());
                                if (info != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(info.Description))
                                    {
                                        importedDescription = info.Description;
                                    }
                                    // 可以选择信任 ZIP 内的时间，或者是导入时间
                                    if (info.CreatedAt != DateTime.MinValue)
                                    {
                                        importedCreatedAt = info.CreatedAt;
                                    } 
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, "Failed to read backup_info.json directly during import");
                        }
                    }
                }
            } 
            catch (Exception ex)
            {
                 throw new InvalidOperationException($"Invalid ZIP file: {ex.Message}", ex);
            }

            // 准备目标路径
            var gameBackupPath = GetGameBackupDirectory(config.ConfigId, gameName);
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
                ConfigId = config.ConfigId,
                GameId = gameId,
                Name = backupName,
                Description = importedDescription,
                BackupFilePath = GetRelativeBackupPath(destPath),
                CreatedAt = importedCreatedAt,
                FileSize = fileInfo.Length
            };

            // 计算导入备份的 CRC32 校验值
            backup.CRC = ComputeCrc32(destPath);

            // 版本历史仅记录当前备份的 CRC
            backup.VersionHistory.Add(backup.CRC);

            // 保存记录
            if (!gameBackups.ContainsKey(config.ConfigId))
            {
                gameBackups[config.ConfigId] = new List<SaveBackup>();
            }
            gameBackups[config.ConfigId].Add(backup);
            SaveData();

            logger.Info($"Imported backup for game {gameName}: {destPath} (CRC: {backup.CRC})");
            return backup;
        }

        /// <summary>
        /// 删除备份
        /// </summary>
        public void DeleteBackup(SaveBackup backup)
        {
            var fullPath = GetFullBackupPath(backup.BackupFilePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            // 优先使用 ConfigId，如果没有则通过 GameId 查找
            var configId = backup.ConfigId != Guid.Empty 
                ? backup.ConfigId 
                : (gameIdToConfigId.TryGetValue(backup.GameId, out var cid) ? cid : Guid.Empty);

            if (configId != Guid.Empty && gameBackups.TryGetValue(configId, out var backups))
            {
                backups.RemoveAll(b => b.Id == backup.Id);
                
                // 如果该游戏没有备份了，清理相关数据和目录
                if (backups.Count == 0)
                {
                    try
                    {
                        var backupDir = Path.GetDirectoryName(fullPath);
                        if (Directory.Exists(backupDir) && !Directory.EnumerateFileSystemEntries(backupDir).Any())
                        {
                            Directory.Delete(backupDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "Failed to delete empty backup directory");
                    }

                    gameBackups.Remove(configId);
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
            // 优先使用 ConfigId
            var configId = backup.ConfigId != Guid.Empty 
                ? backup.ConfigId 
                : (gameIdToConfigId.TryGetValue(backup.GameId, out var cid) ? cid : Guid.Empty);

            // 1. 更新本地缓存的数据
            if (configId != Guid.Empty && gameBackups.TryGetValue(configId, out var backups))
            {
                var existingBackup = backups.FirstOrDefault(b => b.Id == backup.Id);
                if (existingBackup != null)
                {
                    existingBackup.Description = description;
                    
                    // 修改备注后，自动备份变为手动备份（不再受自动清理限制）
                    if (existingBackup.IsAutoBackup)
                    {
                        existingBackup.IsAutoBackup = false;
                        logger.Info($"Backup '{existingBackup.Name}' changed from auto to manual due to note edit");
                    }
                    
                    SaveData();
                }
            }

            // 2. 同步更新 ZIP 文件内的 metadata
            try
            {
                var fullPath = GetFullBackupPath(backup.BackupFilePath);
                if (File.Exists(fullPath))
                {
                    using (var zip = ZipFile.Open(fullPath, ZipArchiveMode.Update))
                    {
                        var entry = zip.GetEntry("backup_info.json");
                        if (entry != null)
                        {
                            entry.Delete(); // 删除旧的，重新创建
                        }

                        entry = zip.CreateEntry("backup_info.json");
                        
                        // 尝试获取游戏名称（用于元数据完整性）
                        var config = GetConfigByConfigId(configId);
                        string gameName = config?.GameName ?? "Unknown Game";

                        var info = new BackupInfo
                        {
                            Description = description,
                            CreatedAt = backup.CreatedAt, // 保持原始创建时间
                            GameName = gameName,
                            Version = 1
                        };

                        using (var writer = new StreamWriter(entry.Open()))
                        {
                            writer.Write(Playnite.SDK.Data.Serialization.ToJson(info, true));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to update backup_info.json in ZIP: {backup.BackupFilePath}");
                // 注意：这里我们记录错误但不抛出异常，因为本地缓存已经更新，这是一个非致命错误
            }
        }

        /// <summary>
        /// 获取游戏的备份目录（使用 ConfigId）
        /// </summary>
        public string GetGameBackupDirectory(Guid configId, string gameName)
        {
            return Path.Combine(backupsPath, SanitizeFileName(gameName) + "_" + configId.ToString("N").Substring(0, 8));
        }

        /// <summary>
        /// 获取游戏的备份目录（通过 GameId 查找）
        /// </summary>
        public string GetGameBackupDirectoryByGameId(Guid gameId, string gameName)
        {
            var config = GetGameConfig(gameId);
            if (config != null)
            {
                return GetGameBackupDirectory(config.ConfigId, gameName);
            }
            // 如果没有配置，返回一个基于 gameId 的临时路径（向后兼容）
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
        /// 清理超出数量限制的旧自动备份
        /// </summary>
        /// <param name="gameId">游戏ID</param>
        /// <param name="maxCount">最大保留数量（0表示不限制）</param>
        public void CleanupOldAutoBackups(Guid gameId, int maxCount)
        {
            if (maxCount <= 0)
            {
                return; // 0 或负数表示不限制
            }

            var config = GetGameConfig(gameId);
            if (config == null || !gameBackups.TryGetValue(config.ConfigId, out var backups))
            {
                return;
            }

            // 只筛选自动备份
            var autoBackups = backups.Where(b => b.IsAutoBackup).ToList();
            
            if (autoBackups.Count <= maxCount)
            {
                return; // 自动备份数量未超出限制
            }

            try
            {
                // 按创建时间排序，最新的在前
                var sortedAutoBackups = autoBackups.OrderByDescending(b => b.CreatedAt).ToList();
                
                // 获取需要删除的自动备份（超出限制的旧备份）
                var backupsToDelete = sortedAutoBackups.Skip(maxCount).ToList();
                
                logger.Info($"Cleaning up {backupsToDelete.Count} old auto-backups for config ID {config.ConfigId}");
                
                foreach (var backup in backupsToDelete)
                {
                    try
                    {
                        var fullPath = GetFullBackupPath(backup.BackupFilePath);
                        // 删除物理文件
                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                        }
                        
                        // 从列表中移除
                        backups.Remove(backup);
                        logger.Info($"Deleted old auto-backup: {backup.Name}");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"Failed to delete old backup: {backup.BackupFilePath}");
                    }
                }
                
                // 保存更新后的数据
                SaveData();
                
                // 检查备份目录是否为空，如果为空则删除
                if (backups.Count == 0)
                {
                    var backupDir = Path.GetDirectoryName(GetFullBackupPath(backupsToDelete.FirstOrDefault()?.BackupFilePath));
                    if (!string.IsNullOrEmpty(backupDir) && Directory.Exists(backupDir) && !Directory.EnumerateFileSystemEntries(backupDir).Any())
                    {
                        Directory.Delete(backupDir);
                    }
                    gameBackups.Remove(config.ConfigId);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to cleanup old backups for config ID {config.ConfigId}");
            }
        }

        #endregion

        #region 数据模型

        /// <summary>
        /// V1 存档数据模型（用于迁移）
        /// </summary>
        public class SaveDataModelV1
        {
            public Dictionary<Guid, GameSaveConfig> GameConfigs { get; set; }
            public Dictionary<Guid, List<SaveBackup>> GameBackups { get; set; }
        }

        /// <summary>
        /// V2 存档数据模型（当前版本）
        /// </summary>
        public class SaveDataModelV2
        {
            public int Version { get; set; } = CurrentDataVersion;
            public Dictionary<Guid, GameSaveConfig> GameConfigs { get; set; }
            public Dictionary<Guid, List<SaveBackup>> GameBackups { get; set; }
        }

        /// <summary>
        /// 路径映射
        /// </summary>
        public class PathMapping
        {
            public string OriginalPath { get; set; }
            public bool IsDirectory { get; set; }
            public string EntryName { get; set; }
        }

        #endregion
    }
}
