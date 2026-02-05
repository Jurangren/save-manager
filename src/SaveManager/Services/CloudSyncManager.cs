using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using SaveManager.Models;

namespace SaveManager.Services
{
    /// <summary>
    /// 云同步管理器 - 处理存档的云端同步逻辑
    /// </summary>
    public class CloudSyncManager
    {
        private readonly RcloneService rcloneService;
        private readonly BackupService backupService;
        private readonly IPlayniteAPI playniteApi;
        private readonly ILogger logger;
        private readonly string dataPath;

        /// <summary>
        /// 获取当前配置的云服务商
        /// </summary>
        public Func<CloudProvider> GetCloudProvider { get; set; }

        /// <summary>
        /// 获取云同步是否启用
        /// </summary>
        public Func<bool> GetCloudSyncEnabled { get; set; }

        /// <summary>
        /// 配置同步结果
        /// </summary>
        public class ConfigSyncResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            /// <summary>
            /// 新增的配置ID列表（之前本地没有的）
            /// </summary>
            public List<Guid> NewConfigIds { get; set; } = new List<Guid>();
        }

        /// <summary>
        /// config.json 在云端的路径
        /// </summary>
        private const string RemoteConfigPath = "config.json";

        public CloudSyncManager(
            string dataPath,
            BackupService backupService,
            RcloneService rcloneService,
            IPlayniteAPI playniteApi,
            ILogger logger)
        {
            this.dataPath = dataPath;
            this.backupService = backupService;
            this.rcloneService = rcloneService;
            this.playniteApi = playniteApi;
            this.logger = logger;
        }

        /// <summary>
        /// 清理旧的 config.json 备份文件，只保留最近的 N 个
        /// </summary>
        private void CleanupOldConfigBackups(string directory, int keepCount)
        {
            try
            {
                var backupFiles = Directory.GetFiles(directory, "config.json.backup_*")
                    .OrderByDescending(f => f)  // 按文件名降序（新的在前）
                    .ToList();

                if (backupFiles.Count <= keepCount) return;

                // 删除超出数量的旧备份
                foreach (var oldFile in backupFiles.Skip(keepCount))
                {
                    try
                    {
                        File.Delete(oldFile);
                        logger.Info($"Deleted old config backup: {Path.GetFileName(oldFile)}");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"Failed to delete old config backup: {oldFile}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to cleanup old config backups");
            }
        }

        #region 启动时同步

        /// <summary>
        /// Playnite 启动时执行的同步操作
        /// </summary>
        public async Task OnApplicationStartedAsync()
        {
            if (GetCloudSyncEnabled?.Invoke() != true)
            {
                logger.Info("Cloud sync is disabled, skipping startup sync");
                return;
            }

            var provider = GetCloudProvider?.Invoke() ?? CloudProvider.GoogleDrive;

            if (!rcloneService.IsRcloneInstalled || !rcloneService.IsConfigured(provider))
            {
                logger.Warn("Rclone not installed or not configured, skipping startup sync");
                return;
            }

            try
            {
                logger.Info("Starting cloud sync on application startup...");

                // 1. 下载云端的 config.json
                await DownloadConfigFromCloudAsync(provider);

                // 2. 检查是否有新游戏需要匹配（这将由调用者处理）
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to sync on application startup");
            }
        }

        /// <summary>
        /// 从云端下载 config.json
        /// </summary>
        private async Task DownloadConfigFromCloudAsync(CloudProvider provider)
        {
            var localConfigPath = Path.Combine(dataPath, "config.json");
            var tempConfigPath = Path.Combine(dataPath, "config_cloud_temp.json");

            try
            {
                // 检查云端是否存在 config.json
                var existsResult = await rcloneService.RemoteFileExistsAsync(RemoteConfigPath, provider);
                
                if (existsResult == null)
                {
                    // 连接失败/超时
                    throw new Exception(ResourceProvider.GetString("LOCSaveManagerMsgCloudConnectionFailed"));
                }
                
                if (existsResult == false)
                {
                    logger.Info("No config.json found on cloud, skipping download");
                    return;
                }

                // 下载到临时文件
                var downloaded = await rcloneService.DownloadFileAsync(
                    RemoteConfigPath,
                    tempConfigPath,
                    provider
                );

                if (downloaded && File.Exists(tempConfigPath))
                {
                    // 备份本地配置
                    if (File.Exists(localConfigPath))
                    {
                        var backupPath = localConfigPath + $".backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                        File.Copy(localConfigPath, backupPath, true);
                        
                        // 清理旧的备份文件，只保留最近10个
                        CleanupOldConfigBackups(dataPath, 10);
                    }

                    // 替换本地配置
                    File.Copy(tempConfigPath, localConfigPath, true);
                    File.Delete(tempConfigPath);

                    // 重新加载配置
                    backupService.ReloadData();

                    logger.Info("Cloud config.json downloaded and applied");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to download config from cloud");

                // 清理临时文件
                if (File.Exists(tempConfigPath))
                {
                    try { File.Delete(tempConfigPath); } catch { }
                }
                
                throw; // 重新抛出异常以便调用者处理
            }
        }

        /// <summary>
        /// Playnite 启动时从云端同步 config.json（公开方法）
        /// </summary>
        public async Task<ConfigSyncResult> SyncConfigFromCloudAsync()
        {
            var result = new ConfigSyncResult { Success = true };

            if (GetCloudSyncEnabled?.Invoke() != true)
            {
                logger.Info("Cloud sync is disabled, skipping config sync");
                return result;
            }

            var provider = GetCloudProvider?.Invoke() ?? CloudProvider.GoogleDrive;

            if (!rcloneService.IsRcloneInstalled || !rcloneService.IsConfigured(provider))
            {
                logger.Warn("Rclone not installed or not configured, skipping config sync");
                return result;
            }

            try
            {
                // 在同步前获取本地已有的配置ID列表
                var existingConfigIds = backupService.GetAllGameConfigs()
                    .Select(c => c.ConfigId)
                    .ToHashSet();

                logger.Info("Syncing config.json from cloud...");
                await DownloadConfigFromCloudAsync(provider);
                logger.Info("Config.json synced from cloud successfully");

                // 同步后检查新增的配置
                var newConfigs = backupService.GetAllGameConfigs()
                    .Where(c => !existingConfigIds.Contains(c.ConfigId))
                    .ToList();

                result.NewConfigIds = newConfigs.Select(c => c.ConfigId).ToList();

                if (result.NewConfigIds.Count > 0)
                {
                    logger.Info($"Found {result.NewConfigIds.Count} new configs from cloud: {string.Join(", ", newConfigs.Select(c => c.GameName))}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to sync config.json from cloud");
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        #endregion

        #region 游戏启动前同步

        /// <summary>
        /// 同步结果枚举
        /// </summary>
        public enum SyncCheckResult
        {
            /// <summary>
            /// 本地和云端一致，可以直接启动
            /// </summary>
            InSync,

            /// <summary>
            /// 本地落后于云端，需要拉取云端存档
            /// </summary>
            LocalBehind,

            /// <summary>
            /// 云端落后于本地，可以直接启动
            /// </summary>
            CloudBehind,

            /// <summary>
            /// 存档冲突，需要用户选择
            /// </summary>
            Conflict,

            /// <summary>
            /// 本地无存档，云端有
            /// </summary>
            LocalMissing,

            /// <summary>
            /// 云端无存档，本地有
            /// </summary>
            CloudMissing,

            /// <summary>
            /// 两边都没有存档
            /// </summary>
            BothMissing,

            /// <summary>
            /// 检查失败
            /// </summary>
            Error
        }

        /// <summary>
        /// 同步检查结果详情
        /// </summary>
        public class SyncCheckInfo
        {
            public SyncCheckResult Result { get; set; }
            public SaveBackup LocalBackup { get; set; }
            public SaveBackup CloudBackup { get; set; }
            public string LocalCRC { get; set; }
            public string CloudCRC { get; set; }
            public string ErrorMessage { get; set; }
            /// <summary>
            /// 本地 Latest.zip 文件的实际修改时间
            /// </summary>
            public DateTime? LocalFileModifiedTime { get; set; }
        }

        /// <summary>
        /// 游戏启动前检查同步状态（不下载任何东西，只用本地 config.json）
        /// 5种情况：
        /// 1. 本地 Latest CRC 在云端 Latest 的 VersionHistory 里 → LocalBehind，需要拉取云端
        /// 2. 云端 Latest CRC 在本地 Latest 的 VersionHistory 里 → CloudBehind，直接启动
        /// 3. 都不符合 → Conflict，让用户选择
        /// 4. 本地无 Latest 远程有 → LocalMissing，需要拉取云端
        /// 5. 远程无 Latest → CloudMissing，直接启动
        /// </summary>
        public async Task<SyncCheckInfo> CheckSyncStatusBeforeGameStartAsync(Guid gameId, string gameName)
        {
            var result = new SyncCheckInfo { Result = SyncCheckResult.Error };

            if (GetCloudSyncEnabled?.Invoke() != true)
            {
                result.Result = SyncCheckResult.InSync;
                return result;
            }

            try
            {
                var config = backupService.GetGameConfig(gameId);
                if (config == null)
                {
                    result.Result = SyncCheckResult.BothMissing;
                    return result;
                }

                // 获取本地 Latest 备份记录（从 config.json 中读取）- 但这是云端的记录
                var localBackups = backupService.GetBackups(gameId);
                var localLatest = localBackups?.FirstOrDefault(b => b.Name == "Latest");

                // 检查实际的 Latest.zip 文件是否存在，并从中读取 backup_info
                bool localLatestFileExists = false;
                BackupInfo localBackupInfo = null;
                string localLatestPath = null;
                
                var localGameBackupPath = backupService.GetGameBackupDirectory(config.ConfigId, gameName);
                localLatestPath = Path.Combine(localGameBackupPath, "Latest.zip");
                localLatestFileExists = File.Exists(localLatestPath);
                
                if (localLatestFileExists)
                {
                    // 从本地 Latest.zip 内的 backup_info.json 读取 CRC 和 VersionHistory
                    // 这是关键：即使 config.json 被云端覆盖，本地 zip 内的信息仍然保留
                    localBackupInfo = backupService.ReadBackupInfoFromZip(localLatestPath);
                    if (localBackupInfo != null)
                    {
                        logger.Info($"Game '{gameName}': Read backup_info from local Latest.zip (CRC: {localBackupInfo.CRC}, History count: {localBackupInfo.VersionHistory?.Count ?? 0})");
                    }
                    else
                    {
                        logger.Warn($"Game '{gameName}': Cannot read backup_info from local Latest.zip");
                    }
                    
                    // 获取本地 Latest.zip 文件的实际修改时间
                    result.LocalFileModifiedTime = File.GetLastWriteTime(localLatestPath);
                }
                else if (localLatest != null)
                {
                    logger.Info($"Game '{gameName}': Latest record exists but file not found at {localLatestPath}");
                }

                // config.json 中的 Latest 就是云端的状态（因为 Playnite 启动时已同步）
                // 这里我们用 CloudLatestCRC 和 CloudVersionHistory 来存储云端状态
                var cloudCRC = config.CloudLatestCRC;
                var cloudVersionHistory = config.CloudVersionHistory;

                // 情况 5: 云端没有 Latest
                if (string.IsNullOrEmpty(cloudCRC))
                {
                    result.Result = localLatestFileExists ? SyncCheckResult.CloudMissing : SyncCheckResult.BothMissing;
                    result.LocalBackup = localLatest;
                    result.LocalCRC = localBackupInfo?.CRC;
                    logger.Info($"Game '{gameName}': Cloud has no Latest, result={result.Result}");
                    return result;
                }

                // 情况 4: 本地没有 Latest 文件，云端有
                if (!localLatestFileExists)
                {
                    result.Result = SyncCheckResult.LocalMissing;
                    result.CloudCRC = cloudCRC;
                    logger.Info($"Game '{gameName}': Local has no Latest file but cloud has, need to pull");
                    return result;
                }

                // 从 backup_info 获取本地 CRC 和 VersionHistory
                var localCRC = localBackupInfo?.CRC;
                var localVersionHistory = localBackupInfo?.VersionHistory;

                // 如果无法从 backup_info 读取（旧版本备份），尝试重新计算
                if (string.IsNullOrEmpty(localCRC) && localLatestFileExists)
                {
                    localCRC = backupService.ComputeCrc32(localLatestPath);
                    logger.Info($"Game '{gameName}': Computed CRC from local Latest.zip: {localCRC}");
                }

                result.LocalBackup = localLatest;
                result.LocalCRC = localCRC;
                result.CloudCRC = cloudCRC;

                // CRC 相同，已同步
                if (localCRC == cloudCRC)
                {
                    result.Result = SyncCheckResult.InSync;
                    logger.Info($"Game '{gameName}': In sync (CRC: {cloudCRC})");
                    return result;
                }

                // 情况 1: 本地 CRC 在云端 VersionHistory 中 → 本地落后，需要拉取云端
                if (cloudVersionHistory != null && !string.IsNullOrEmpty(localCRC) && cloudVersionHistory.Contains(localCRC))
                {
                    result.Result = SyncCheckResult.LocalBehind;
                    logger.Info($"Game '{gameName}': Local is behind cloud (local CRC {localCRC} found in cloud history)");
                    return result;
                }

                // 情况 2: 云端 CRC 在本地 VersionHistory 中 → 云端落后，直接启动
                if (localVersionHistory != null && localVersionHistory.Contains(cloudCRC))
                {
                    result.Result = SyncCheckResult.CloudBehind;
                    logger.Info($"Game '{gameName}': Cloud is behind local (cloud CRC {cloudCRC} found in local history)");
                    return result;
                }

                // 情况 3: 冲突 - 需要构建云端备份信息用于显示
                result.Result = SyncCheckResult.Conflict;
                // 从 config.json 中构建云端备份信息
                result.CloudBackup = new SaveBackup
                {
                    Name = "Latest (Cloud)",
                    CRC = cloudCRC,
                    CreatedAt = config.CloudLatestTime ?? DateTime.MinValue,
                    FileSize = config.CloudLatestSize ?? 0,
                    VersionHistory = cloudVersionHistory != null ? new List<string>(cloudVersionHistory) : new List<string>()
                };
                logger.Info($"Game '{gameName}': Conflict detected (local CRC: {localCRC}, cloud CRC: {cloudCRC})");
                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to check sync status");
                result.ErrorMessage = ex.Message;
                result.Result = SyncCheckResult.InSync; // 出错时不阻止游戏启动
                return result;
            }
        }
        /// <summary>
        /// 从云端拉取并还原 Latest 存档
        /// </summary>
        public async Task<bool> PullAndRestoreLatestAsync(Guid gameId, string gameName)
        {
            if (GetCloudSyncEnabled?.Invoke() != true) return false;

            var provider = GetCloudProvider?.Invoke() ?? CloudProvider.GoogleDrive;

            try
            {
                var config = backupService.GetGameConfig(gameId);
                if (config == null) return false;

                var remoteGamePath = rcloneService.GetRemoteGamePath(config.ConfigId, gameName);
                var remoteLatestPath = $"{remoteGamePath}/Latest.zip";

                // 本地目标路径
                var localGameBackupPath = backupService.GetGameBackupDirectory(config.ConfigId, gameName);
                var localLatestPath = Path.Combine(localGameBackupPath, "Latest.zip");

                // 下载 Latest.zip
                logger.Info($"Pulling Latest.zip from cloud for {gameName}...");
                var downloaded = await rcloneService.DownloadFileAsync(remoteLatestPath, localLatestPath, provider);

                if (!downloaded)
                {
                    logger.Error("Failed to download Latest.zip from cloud");
                    return false;
                }

                // 从下载的 Latest.zip 中提取 backup_info.json 并更新本地记录
                var zipPath = localLatestPath;
                if (File.Exists(zipPath))
                {
                    try
                    {
                        using (var archive = System.IO.Compression.ZipFile.OpenRead(zipPath))
                        {
                            var infoEntry = archive.GetEntry("backup_info.json");
                            if (infoEntry != null)
                            {
                                using (var stream = infoEntry.Open())
                                using (var reader = new StreamReader(stream))
                                {
                                    var json = reader.ReadToEnd();
                                    var cloudBackup = Playnite.SDK.Data.Serialization.FromJson<SaveBackup>(json);
                                    if (cloudBackup != null)
                                    {
                                        // 更新本地备份记录
                                        backupService.UpdateOrAddBackup(cloudBackup);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "Failed to read backup_info.json from downloaded Latest.zip");
                    }
                }

                // 执行还原（强制还原，不使用排除项）
                var latestBackup = backupService.GetBackups(gameId)?.FirstOrDefault(b => b.Name == "Latest");
                if (latestBackup != null)
                {
                    backupService.RestoreBackup(latestBackup, null);
                    logger.Info($"Cloud Latest.zip restored for {gameName}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to pull and restore Latest for {gameName}");
                return false;
            }
        }

        #endregion

        #region 上传操作

        /// <summary>
        /// 上传 config.json 到云端（后台执行）
        /// </summary>
        public async Task UploadConfigToCloudAsync()
        {
            if (GetCloudSyncEnabled?.Invoke() != true) return;

            var provider = GetCloudProvider?.Invoke() ?? CloudProvider.GoogleDrive;

            try
            {
                var localConfigPath = Path.Combine(dataPath, "config.json");
                if (!File.Exists(localConfigPath))
                {
                    logger.Warn("Local config.json not found, skipping upload");
                    return;
                }

                logger.Info("Uploading config.json to cloud...");
                var success = await rcloneService.UploadFileAsync(localConfigPath, RemoteConfigPath, provider);

                if (success)
                {
                    logger.Info("config.json uploaded to cloud successfully");
                }
                else
                {
                    logger.Error("Failed to upload config.json to cloud");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to upload config to cloud");
            }
        }

        /// <summary>
        /// 上传备份文件到云端
        /// </summary>
        public async Task<bool> UploadBackupToCloudAsync(SaveBackup backup, string gameName)
        {
            if (GetCloudSyncEnabled?.Invoke() != true) return false;

            var provider = GetCloudProvider?.Invoke() ?? CloudProvider.GoogleDrive;

            try
            {
                var localBackupPath = backupService.GetFullBackupPath(backup.BackupFilePath);
                if (!File.Exists(localBackupPath))
                {
                    logger.Error($"Local backup file not found: {localBackupPath}");
                    return false;
                }

                var remoteGamePath = rcloneService.GetRemoteGamePath(backup.ConfigId, gameName);
                var remoteBackupPath = $"{remoteGamePath}/{Path.GetFileName(backup.BackupFilePath)}";

                logger.Info($"Uploading backup {backup.Name} to cloud...");
                var success = await rcloneService.UploadFileAsync(localBackupPath, remoteBackupPath, provider);

                if (success)
                {
                    // Latest 备份上传后，额外更新 config.json 中的云端 Latest 信息
                    if (backup.Name == "Latest")
                    {
                        // 更新 config.json 中该游戏的云端 Latest 信息
                        var config = backupService.GetConfigByConfigId(backup.ConfigId);
                        if (config != null)
                        {
                            config.CloudLatestCRC = backup.CRC;
                            config.CloudVersionHistory = backup.VersionHistory != null 
                                ? new List<string>(backup.VersionHistory) 
                                : new List<string>();
                            config.CloudLatestTime = backup.CreatedAt;
                            config.CloudLatestSize = backup.FileSize;
                            backupService.SaveGameConfig(config);
                        }
                    }

                    // 所有备份上传后都要同步 config.json，确保备份记录同步到云端
                    await UploadConfigToCloudAsync();

                    logger.Info($"Backup {backup.Name} uploaded to cloud successfully");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to upload backup {backup.Name} to cloud");
                return false;
            }
        }

        /// <summary>
        /// 从云端删除备份文件
        /// </summary>
        public async Task<bool> DeleteBackupFromCloudAsync(SaveBackup backup, string gameName = null)
        {
            if (GetCloudSyncEnabled?.Invoke() != true) return false;

            var provider = GetCloudProvider?.Invoke() ?? CloudProvider.GoogleDrive;

            try
            {
                var remoteGamePath = rcloneService.GetRemoteGamePath(backup.ConfigId);
                var remoteBackupPath = $"{remoteGamePath}/{Path.GetFileName(backup.BackupFilePath)}";

                logger.Info($"Deleting backup {backup.Name} from cloud...");
                var success = await rcloneService.DeleteRemoteFileAsync(remoteBackupPath, provider);

                if (success)
                {
                    logger.Info($"Backup {backup.Name} deleted from cloud successfully");
                    
                    // 如果删除的是 Latest，额外清除 config.json 中的云端 Latest 信息
                    if (backup.Name == "Latest")
                    {
                        var config = backupService.GetConfigByConfigId(backup.ConfigId);
                        if (config != null)
                        {
                            config.CloudLatestCRC = null;
                            config.CloudVersionHistory = new List<string>();
                            config.CloudLatestTime = null;
                            config.CloudLatestSize = 0;
                            backupService.SaveGameConfig(config);
                        }
                    }
                    
                    // 所有备份删除后都要同步 config.json，确保备份记录同步到云端
                    await UploadConfigToCloudAsync();
                    
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to delete backup {backup.Name} from cloud");
                return false;
            }
        }

        /// <summary>
        /// 首次同步：上传所有本地备份到云端（包括所有历史备份）
        /// </summary>
        public async Task<int> UploadAllBackupsToCloudAsync(
            IProgress<string> progress = null, 
            CancellationToken cancellationToken = default,
            Action<int, int> progressCallback = null)
        {
            if (GetCloudSyncEnabled?.Invoke() != true) return 0;

            var provider = GetCloudProvider?.Invoke() ?? CloudProvider.GoogleDrive;
            int uploadedCount = 0;

            try
            {
                // 确保云端根目录存在
                await rcloneService.EnsureRemoteRootDirectoryAsync(provider);
                
                // 获取所有游戏配置
                var allConfigs = backupService.GetAllGameConfigs();
                if (allConfigs == null || allConfigs.Count == 0)
                {
                    progress?.Report("No backups to upload");
                    return 0;
                }

                // 计算总备份数
                int totalBackups = 0;
                foreach (var config in allConfigs)
                {
                    var backups = backupService.GetBackupsByConfigId(config.ConfigId);
                    if (backups != null) totalBackups += backups.Count;
                }
                totalBackups++; // 加上 config.json

                int currentProgress = 0;

                foreach (var config in allConfigs)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var backups = backupService.GetBackupsByConfigId(config.ConfigId);
                    if (backups == null || backups.Count == 0) continue;

                    // 上传该游戏的所有备份
                    foreach (var backup in backups)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        currentProgress++;
                        progressCallback?.Invoke(currentProgress, totalBackups);
                        progress?.Report($"Uploading {config.GameName} - {backup.Name}...");

                        var success = await UploadBackupToCloudAsync(backup, config.GameName);
                        if (success) uploadedCount++;
                    }
                }

                // 最后上传 config.json
                currentProgress++;
                progressCallback?.Invoke(currentProgress, totalBackups);
                progress?.Report("Uploading config.json...");
                await UploadConfigToCloudAsync();

                progress?.Report($"Uploaded {uploadedCount} backups to cloud");
                logger.Info($"First sync completed: uploaded {uploadedCount} backups");
                return uploadedCount;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to upload all backups to cloud");
                progress?.Report($"Error: {ex.Message}");
                return uploadedCount;
            }
        }

        #endregion

        #region 拉取所有数据

        /// <summary>
        /// 从云端拉取所有数据
        /// </summary>
        public async Task<bool> PullAllDataFromCloudAsync(
            IProgress<string> progress = null, 
            CancellationToken cancellationToken = default,
            Action<int, int> progressCallback = null)
        {
            if (GetCloudSyncEnabled?.Invoke() != true) return false;

            var provider = GetCloudProvider?.Invoke() ?? CloudProvider.GoogleDrive;
            const int totalSteps = 3;

            try
            {
                // 步骤 1: 下载 config.json
                progressCallback?.Invoke(1, totalSteps);
                progress?.Report("Downloading config.json...");
                await DownloadConfigFromCloudAsync(provider);

                if (cancellationToken.IsCancellationRequested) return false;

                // 步骤 2: 检查云端 Backups 目录是否存在
                progressCallback?.Invoke(2, totalSteps);
                progress?.Report("Checking cloud backups...");
                var backupsExist = await rcloneService.RemoteFileExistsAsync("Backups", provider);

                if (backupsExist == null)
                {
                    throw new Exception(ResourceProvider.GetString("LOCSaveManagerMsgCloudConnectionFailed"));
                }

                if (backupsExist == true)
                {
                    // 下载所有备份文件夹（排除 Latest.zip，启动游戏时会自动下载）
                    progress?.Report("Downloading backup history (Latest will sync on game start)...");
                    var localBackupsPath = Path.Combine(dataPath, "Backups");
                    var success = await rcloneService.DownloadDirectoryAsync(
                        "Backups", 
                        localBackupsPath, 
                        provider, 
                        cancellationToken,
                        "*/Latest.zip");  // 排除所有 Latest.zip

                    if (success)
                    {
                        // 重新加载数据
                        backupService.ReloadData();
                        progress?.Report("Backup history pulled! Latest saves will sync when games start.");
                        logger.Info("All backup history pulled from cloud (Latest.zip excluded)");
                    }
                    else
                    {
                        logger.Warn("Failed to download backups from cloud");
                    }
                }
                else
                {
                    // 云端没有备份目录，这是正常的（首次使用）
                    logger.Info("No Backups directory found on cloud, skipping download");
                    progress?.Report("No backups found on cloud (first time use)");
                }

                // 步骤 3: 完成
                progressCallback?.Invoke(3, totalSteps);
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to pull all data from cloud");
                progress?.Report($"Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 仅从云端拉取配置文件（不拉取备份）
        /// </summary>
        public async Task<bool> PullConfigOnlyAsync(CloudProvider provider, CancellationToken cancellationToken = default)
        {
            try
            {
                await DownloadConfigFromCloudAsync(provider);
                backupService.ReloadData();
                logger.Info("Config pulled from cloud (backups not downloaded)");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to pull config from cloud");
                return false;
            }
        }

        /// <summary>
        /// 将本地的 Latest 快照推送到云端（用于冲突时保留本地）
        /// </summary>
        public async Task<bool> PushLatestToCloudAsync(Guid gameId, string gameName)
        {
            if (GetCloudSyncEnabled?.Invoke() != true) return false;

            var provider = GetCloudProvider?.Invoke() ?? CloudProvider.GoogleDrive;

            try
            {
                var config = backupService.GetGameConfig(gameId);
                if (config == null)
                {
                    logger.Warn($"Cannot push: no config for game {gameName}");
                    return false;
                }

                // 创建新的 Latest 快照
                var latestBackup = backupService.CreateRealtimeSyncSnapshot(gameId, gameName);
                if (latestBackup == null)
                {
                    logger.Warn($"Failed to create Latest snapshot for {gameName}");
                    return false;
                }

                // 上传 Latest.zip
                var localLatestPath = backupService.GetFullBackupPath(latestBackup.BackupFilePath);
                var remoteGamePath = rcloneService.GetRemoteGamePath(config.ConfigId, gameName);
                var remoteLatestPath = $"{remoteGamePath}/Latest.zip";

                var uploadSuccess = await rcloneService.UploadFileAsync(localLatestPath, remoteLatestPath, provider);
                if (!uploadSuccess)
                {
                    logger.Warn($"Failed to upload Latest.zip for {gameName}");
                    return false;
                }

                // 上传 config.json
                await UploadConfigToCloudAsync();

                logger.Info($"Pushed local Latest to cloud for game {gameName}");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to push Latest to cloud for game {gameName}");
                return false;
            }
        }

        #endregion
    }
}
