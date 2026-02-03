using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SaveManager.Models;
using SaveManager.Services;
using SaveManager.ViewModels;
using SaveManager.Views;

namespace SaveManager
{
    /// <summary>
    /// Playnite Save Manager 插件主类
    /// </summary>
    public class SaveManagerPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        
        private SaveManagerSettings settings;
        private BackupService backupService;
        private Services.RcloneService rcloneService;
        private Services.CloudSyncManager cloudSyncManager;

        public override Guid Id { get; } = Guid.Parse("e8b2f7a1-8c3d-4f5e-9a6b-1c2d3e4f5a6b");

        /// <summary>
        /// 插件设置
        /// </summary>
        public SaveManagerSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
            }
        }

        public SaveManagerPlugin(IPlayniteAPI api) : base(api)
        {
            // 初始化设置
            settings = new SaveManagerSettings(this);
            
            // 初始化服务
            var dataPath = GetPluginUserDataPath();
            backupService = new BackupService(dataPath, logger, PlayniteApi, () => settings.RealtimeSyncEnabled);

            // 初始化 Rclone 服务
            rcloneService = new Services.RcloneService(dataPath, logger, PlayniteApi);

            // 初始化云同步管理器
            cloudSyncManager = new Services.CloudSyncManager(dataPath, backupService, rcloneService, PlayniteApi, logger);
            cloudSyncManager.GetCloudProvider = () => (Models.CloudProvider)settings.CloudProvider;
            cloudSyncManager.GetCloudSyncEnabled = () => settings.CloudSyncEnabled;

            // 设置属性以启用设置视图
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            logger.Info("Save Manager plugin initialized");
        }

        /// <summary>
        /// 获取 Rclone 服务实例
        /// </summary>
        public Services.RcloneService GetRcloneService() => rcloneService;

        /// <summary>
        /// 获取云同步管理器实例
        /// </summary>
        public Services.CloudSyncManager GetCloudSyncManager() => cloudSyncManager;


        /// <summary>
        /// 游戏右键菜单项
        /// </summary>
        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var menuSection = ResourceProvider.GetString("LOCSaveManagerMenuSection");
            
            yield return new GameMenuItem
            {
                Description = ResourceProvider.GetString("LOCSaveManagerSubtitle"),
                MenuSection = menuSection,
                Icon = "💾",
                Action = (menuArgs) =>
                {
                    if (menuArgs.Games.Count == 1)
                    {
                        OpenSaveManager(menuArgs.Games[0]);
                    }
                    else
                    {
                        PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgSelectOneGame"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            };

            yield return new GameMenuItem
            {
                Description = ResourceProvider.GetString("LOCSaveManagerMenuQuickBackup"),
                MenuSection = menuSection,
                Icon = "📦",
                Action = (menuArgs) =>
                {
                    foreach (var game in menuArgs.Games)
                    {
                        QuickBackup(game);
                    }
                }
            };

            // 还原备份 - 二级菜单
            if (args.Games.Count == 1)
            {
                var game = args.Games[0];
                var restoreMenuSection = menuSection + "|" + ResourceProvider.GetString("LOCSaveManagerMenuRestoreBackup");
                var backups = backupService.GetBackups(game.Id)
                    .Where(b => b.Name != "Latest")
                    .ToList();
                
                if (backups.Count == 0)
                {
                    // 无备份时显示提示
                    yield return new GameMenuItem
                    {
                        Description = ResourceProvider.GetString("LOCSaveManagerTitleNoBackups"),
                        MenuSection = restoreMenuSection,
                        Icon = "↩️",
                        Action = null
                    };
                }
                else
                {
                    // 显示最多9个备份
                    var displayCount = Math.Min(backups.Count, 9);
                    for (int i = 0; i < displayCount; i++)
                    {
                        var backup = backups[i];
                        var displayText = string.IsNullOrEmpty(backup.Description) 
                            ? backup.Name 
                            : backup.Description;
                        var subText = backup.FormattedDate;
                        
                        yield return new GameMenuItem
                        {
                            Description = $"{displayText}  ({subText})",
                            MenuSection = restoreMenuSection,
                            Icon = "📁",
                            Action = (menuArgs) =>
                            {
                                RestoreSpecificBackup(game, backup);
                            }
                        };
                    }
                    
                    // 超过9个时显示"查找所有备份"
                    if (backups.Count > 9)
                    {
                        yield return new GameMenuItem
                        {
                            Description = "─────────────────",
                            MenuSection = restoreMenuSection,
                            Action = null
                        };
                        
                        yield return new GameMenuItem
                        {
                            Description = ResourceProvider.GetString("LOCSaveManagerMenuViewAllBackups"),
                            MenuSection = restoreMenuSection,
                            Icon = "🔍",
                            Action = (menuArgs) =>
                            {
                                OpenSaveManager(game);
                            }
                        };
                    }
                }
            }
            else
            {
                // 多选时显示提示
                yield return new GameMenuItem
                {
                    Description = ResourceProvider.GetString("LOCSaveManagerMenuRestoreBackup"),
                    MenuSection = menuSection,
                    Icon = "↩️",
                    Action = (menuArgs) =>
                    {
                        PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgSelectOneGameRestore"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                };
            }
        }

        /// <summary>
        /// 主菜单项
        /// </summary>
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            var menuSection = "@" + ResourceProvider.GetString("LOCSaveManagerMenuSection");
            
            // 导出全局配置
            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCSaveManagerMenuExportGlobalConfig"),
                MenuSection = menuSection,
                Action = (menuArgs) => ExportGlobalConfig()
            };

            // 导入全局配置
            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCSaveManagerMenuImportGlobalConfig"),
                MenuSection = menuSection,
                Action = (menuArgs) => ImportGlobalConfig()
            };

            // 游戏匹配
            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCSaveManagerGameMatching"),
                MenuSection = menuSection,
                Action = (menuArgs) => OpenGameMatchingWindow(fullMode: true)
            };

            // 打开备份文件夹
            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCSaveManagerOpenBackupFolder"),
                MenuSection = menuSection,
                Action = (menuArgs) =>
                {
                    var backupsPath = System.IO.Path.Combine(GetPluginUserDataPath(), "Backups");
                    System.IO.Directory.CreateDirectory(backupsPath);
                    System.Diagnostics.Process.Start("explorer.exe", backupsPath);
                }
            };

            // 关于
            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCSaveManagerMenuAbout"),
                MenuSection = menuSection,
                Action = (menuArgs) =>
                {
                    PlayniteApi.Dialogs.ShowMessage(
                        ResourceProvider.GetString("LOCSaveManagerAboutContent"),
                        ResourceProvider.GetString("LOCSaveManagerMenuAbout"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            };
        }

        /// <summary>
        /// 打开存档管理器窗口
        /// </summary>
        private void OpenSaveManager(Game game)
        {
            try
            {
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false
                });

                window.Width = 900;
                window.Height = 650;
                window.Title = string.Format(ResourceProvider.GetString("LOCSaveManagerWindowTitle"), game.Name);
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();

                var viewModel = new SaveManagerViewModel(game, PlayniteApi, backupService, cloudSyncManager, rcloneService);
                var view = new SaveManagerView
                {
                    DataContext = viewModel
                };

                window.Content = view;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to open Save Manager window");
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 快速备份
        /// </summary>
        private void QuickBackup(Game game)
        {
            try
            {
                var config = backupService.GetGameConfig(game.Id);
                if (config == null || config.SavePaths.Count == 0)
                {
                    var result = PlayniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgNoConfig"), game.Name),
                        ResourceProvider.GetString("LOCSaveManagerTitleNoConfig"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        OpenSaveManager(game);
                    }
                    return;
                }



                // 获取备份备注
                var noteResult = PlayniteApi.Dialogs.SelectString(
                    ResourceProvider.GetString("LOCSaveManagerMsgEnterNote"),
                    ResourceProvider.GetString("LOCSaveManagerTitleBackupNote"),
                    ResourceProvider.GetString("LOCSaveManagerTitleQuickBackup"));

                if (!noteResult.Result)
                {
                    return;
                }

                var backup = backupService.CreateBackup(game.Id, game.Name, noteResult.SelectedString);
                PlayniteApi.Dialogs.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgBackupSuccess"), backup.Name, backup.FormattedSize),
                    "Save Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Quick backup failed for game {game.Name}");
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 快速还原（最近一次备份）
        /// </summary>
        private void QuickRestore(Game game)
        {
            try
            {
                var backups = backupService.GetBackups(game.Id);
                if (backups.Count == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgNoBackups"), game.Name),
                        ResourceProvider.GetString("LOCSaveManagerTitleNoBackups"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var latestBackup = backups[0];
                var result = PlayniteApi.Dialogs.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgConfirmQuickRestore"), 
                        latestBackup.Name, 
                        latestBackup.FormattedDate, 
                        (string.IsNullOrEmpty(latestBackup.Description) ? "-" : latestBackup.Description)),
                    ResourceProvider.GetString("LOCSaveManagerTitleConfirmRestore"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    // 获取排除项配置
                    var config = backupService.GetGameConfig(game.Id);
                    var excludePaths = config?.RestoreExcludePaths;

                    backupService.RestoreBackup(latestBackup, excludePaths);
                    PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgRestoreSuccess"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Quick restore failed for game {game.Name}");
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 还原指定备份（从右键菜单调用）
        /// </summary>
        private void RestoreSpecificBackup(Game game, SaveBackup backup)
        {
            try
            {
                var displayName = string.IsNullOrEmpty(backup.Description) ? backup.Name : backup.Description;
                var result = PlayniteApi.Dialogs.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgConfirmRestoreNamed"), displayName),
                    ResourceProvider.GetString("LOCSaveManagerTitleConfirmRestore"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    // 获取排除项配置
                    var config = backupService.GetGameConfig(game.Id);
                    var excludePaths = config?.RestoreExcludePaths;

                    backupService.RestoreBackup(backup, excludePaths);
                    PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgRestoreSuccess"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Restore backup failed for game {game.Name}");
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 获取设置对象
        /// </summary>
        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        /// <summary>
        /// 获取设置视图
        /// </summary>
        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new SaveManagerSettingsView();
        }

        /// <summary>
        /// 应用启动时触发 - 从云端同步 config.json（强制等待）
        /// </summary>
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (settings.CloudSyncEnabled)
            {
                try
                {
                    // 使用进度对话框同步等待
                    PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                    {
                        try
                        {
                            var task = System.Threading.Tasks.Task.Run(async () =>
                                await cloudSyncManager.SyncConfigFromCloudAsync()
                            );

                            while (!task.IsCompleted && !progressArgs.CancelToken.IsCancellationRequested)
                            {
                                System.Threading.Thread.Sleep(100);
                            }

                            if (task.IsFaulted)
                            {
                                logger.Error(task.Exception, "Failed to sync config from cloud");
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Cloud sync on application started failed");
                        }
                    }, new GlobalProgressOptions(
                        ResourceProvider.GetString("LOCSaveManagerMsgSyncingConfig"), false)
                    {
                        IsIndeterminate = true
                    });
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Cloud sync on application started failed");
                }
            }
        }

        /// <summary>
        /// 游戏启动前触发 - 用于云端存档同步检查
        /// </summary>
        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            var game = args.Game;
            if (game == null || !settings.CloudSyncEnabled || !settings.RealtimeSyncEnabled)
            {
                return;
            }

            try
            {
                // 使用 Task.Run 避免 UI 线程死锁
                var syncInfo = System.Threading.Tasks.Task.Run(async () => 
                    await cloudSyncManager.CheckSyncStatusBeforeGameStartAsync(game.Id, game.Name)
                ).GetAwaiter().GetResult();

                switch (syncInfo.Result)
                {
                    case Services.CloudSyncManager.SyncCheckResult.InSync:
                    case Services.CloudSyncManager.SyncCheckResult.CloudMissing:
                    case Services.CloudSyncManager.SyncCheckResult.BothMissing:
                    case Services.CloudSyncManager.SyncCheckResult.CloudBehind:
                        // 可以直接启动
                        logger.Info($"Cloud sync check passed for game '{game.Name}': {syncInfo.Result}");
                        break;

                    case Services.CloudSyncManager.SyncCheckResult.LocalBehind:
                    case Services.CloudSyncManager.SyncCheckResult.LocalMissing:
                        // 需要拉取云端存档
                        logger.Info($"Cloud sync: pulling latest save for game '{game.Name}'");
                        
                        bool pullResult = false;
                        PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                        {
                            progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgPullingLatest");
                            progressArgs.IsIndeterminate = true;

                            var task = cloudSyncManager.PullAndRestoreLatestAsync(game.Id, game.Name);
                            task.Wait();
                            pullResult = task.Result;
                        }, new GlobalProgressOptions(
                            ResourceProvider.GetString("LOCSaveManagerMsgPullingLatest"), false)
                        {
                            IsIndeterminate = true
                        });

                        if (pullResult)
                        {
                            PlayniteApi.Notifications.Add(new NotificationMessage(
                                $"SaveManager_CloudSync_{game.Id}",
                                ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncPulled"),
                                NotificationType.Info));
                        }
                        else
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage(
                                ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncPullFailed"),
                                "Cloud Sync Error");
                        }
                        break;

                    case Services.CloudSyncManager.SyncCheckResult.Conflict:
                        // 存档冲突，让用户选择
                        var localDate = syncInfo.LocalBackup?.FormattedDate ?? "Unknown";
                        var localSize = syncInfo.LocalBackup?.FormattedSize ?? "Unknown";
                        // 云端信息: 如果时间是 MinValue 说明没有记录
                        var cloudDate = (syncInfo.CloudBackup?.CreatedAt > DateTime.MinValue) 
                            ? syncInfo.CloudBackup.FormattedDate 
                            : "Unknown";
                        var cloudSize = (syncInfo.CloudBackup?.FileSize > 0) 
                            ? syncInfo.CloudBackup.FormattedSize 
                            : "Unknown";
                        
                        // 三按钮对话框：拉取云端、保留本地并推送、取消
                        var conflictOptions = new List<Playnite.SDK.MessageBoxOption>
                        {
                            new Playnite.SDK.MessageBoxOption(
                                ResourceProvider.GetString("LOCSaveManagerBtnPullCloud"), false, false),
                            new Playnite.SDK.MessageBoxOption(
                                ResourceProvider.GetString("LOCSaveManagerBtnKeepLocalAndPush"), true, false),
                            new Playnite.SDK.MessageBoxOption(
                                ResourceProvider.GetString("LOCSaveManagerBtnCancel"), false, true)
                        };

                        var conflictResult = PlayniteApi.Dialogs.ShowMessage(
                            string.Format(
                                ResourceProvider.GetString("LOCSaveManagerMsgSaveConflictOptions"),
                                localDate,
                                localSize,
                                cloudDate,
                                cloudSize),
                            ResourceProvider.GetString("LOCSaveManagerTitleSaveConflict"),
                            MessageBoxImage.Warning,
                            conflictOptions);

                        if (conflictResult == conflictOptions[0])
                        {
                            // 用户选择拉取云端存档
                            bool pullSuccess = false;
                            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                            {
                                progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgPullingLatest");
                                progressArgs.IsIndeterminate = true;

                                var task = cloudSyncManager.PullAndRestoreLatestAsync(game.Id, game.Name);
                                task.Wait();
                                pullSuccess = task.Result;
                            }, new GlobalProgressOptions(
                                ResourceProvider.GetString("LOCSaveManagerMsgPullingLatest"), false)
                            {
                                IsIndeterminate = true
                            });

                            if (pullSuccess)
                            {
                                PlayniteApi.Notifications.Add(new NotificationMessage(
                                    $"SaveManager_CloudSync_{game.Id}",
                                    ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncPulled"),
                                    NotificationType.Info));
                            }
                        }
                        else if (conflictResult == conflictOptions[1])
                        {
                            // 用户选择保留本地并推送到云端
                            bool pushSuccess = false;
                            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                            {
                                progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgPushingLocal");
                                progressArgs.IsIndeterminate = true;

                                // 先创建新的 Latest 快照，然后推送
                                var task = cloudSyncManager.PushLatestToCloudAsync(game.Id, game.Name);
                                task.Wait();
                                pushSuccess = task.Result;
                            }, new GlobalProgressOptions(
                                ResourceProvider.GetString("LOCSaveManagerMsgPushingLocal"), false)
                            {
                                IsIndeterminate = true
                            });

                            if (pushSuccess)
                            {
                                PlayniteApi.Notifications.Add(new NotificationMessage(
                                    $"SaveManager_CloudSync_{game.Id}",
                                    ResourceProvider.GetString("LOCSaveManagerMsgLocalPushed"),
                                    NotificationType.Info));
                            }
                        }
                        // 取消：不做任何操作，直接继续启动
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Cloud sync check failed for game '{game.Name}'");
                // 不阻止游戏启动
            }
        }

        /// <summary>
        /// 游戏停止时触发 - 用于自动备份和实时同步
        /// </summary>
        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            var game = args.Game;
            if (game == null)
            {
                return;
            }

            try
            {
                // 检查游戏是否已配置存档路径
                var config = backupService.GetGameConfig(game.Id);
                if (config == null || config.SavePaths == null || config.SavePaths.Count == 0)
                {
                    logger.Info($"Skipped backup for game '{game.Name}': no save paths configured");
                    return;
                }

                // 1. 处理自动备份
                if (settings.AutoBackupOnGameExit)
                {
                    try
                    {
                        // 创建自动备份
                        var elapsedMinutes = args.ElapsedSeconds / 60;
                        var noteText = string.Format(
                            ResourceProvider.GetString("LOCSaveManagerAutoBackupNote"),
                            elapsedMinutes);
                        
                        var backup = backupService.CreateBackup(game.Id, game.Name, noteText, isAutoBackup: true);
                        
                        logger.Info($"Auto backup created for game '{game.Name}': {backup.Name}");

                        // 清理超出数量限制的旧自动备份
                        backupService.CleanupOldAutoBackups(game.Id, settings.MaxAutoBackupCount);

                        // 显示 Playnite 内置通知
                        PlayniteApi.Notifications.Add(new NotificationMessage(
                            $"SaveManager_AutoBackup_{game.Id}",
                            string.Format(ResourceProvider.GetString("LOCSaveManagerAutoBackupSuccess"), game.Name, backup.Name),
                            NotificationType.Info));

                        // 显示 Windows Toast 通知
                        ToastNotificationService.ShowBackupSuccess(game.Name, backup.Name, game.Icon);
                    }
                    catch (Exception autoEx)
                    {
                        logger.Error(autoEx, $"Auto backup failed for game '{game.Name}'");
                        
                        // 显示 Playnite 内置错误通知
                        PlayniteApi.Notifications.Add(new NotificationMessage(
                            $"SaveManager_AutoBackupError_{game.Id}",
                            string.Format(ResourceProvider.GetString("LOCSaveManagerAutoBackupFailed"), game.Name, autoEx.Message),
                            NotificationType.Error));

                        // 显示 Windows Toast 错误通知
                        ToastNotificationService.ShowBackupError(game.Name, autoEx.Message);
                    }
                }

                // 2. 处理实时同步快照 (独立于自动备份)
                if (settings.RealtimeSyncEnabled)
                {
                    try
                    {
                        var syncBackup = backupService.CreateRealtimeSyncSnapshot(game.Id, game.Name);
                        logger.Info($"Realtime sync snapshot created for game '{game.Name}': {syncBackup.Name} (History: {syncBackup.VersionHistory.Count} versions)");

                        // 3. 如果启用了云同步，上传 Latest.zip 到云端
                        if (settings.CloudSyncEnabled)
                        {
                            // 异步上传，不阻塞
                            System.Threading.Tasks.Task.Run(async () =>
                            {
                                try
                                {
                                    var success = await cloudSyncManager.UploadBackupToCloudAsync(syncBackup, game.Name);
                                    if (success)
                                    {
                                        logger.Info($"Latest.zip uploaded to cloud for game '{game.Name}'");
                                        
                                        // 云上传成功后才显示通知
                                        PlayniteApi.Notifications.Add(new NotificationMessage(
                                            $"SaveManager_CloudUpload_{game.Id}",
                                            ResourceProvider.GetString("LOCSaveManagerMsgCloudUploadSuccess"),
                                            NotificationType.Info));

                                        // 显示 Windows Toast 通知
                                        ToastNotificationService.ShowBackupSuccess(game.Name, "Latest.zip (Cloud)", game.Icon);
                                    }
                                    else
                                    {
                                        logger.Warn($"Failed to upload Latest.zip to cloud for game '{game.Name}'");
                                        
                                        // 显示本地备份成功的通知（云上传失败）
                                        PlayniteApi.Notifications.Add(new NotificationMessage(
                                            $"SaveManager_RealtimeSync_{game.Id}",
                                            ResourceProvider.GetString("LOCSaveManagerMsgRealtimeSyncCreated"),
                                            NotificationType.Info));
                                    }
                                }
                                catch (Exception cloudEx)
                                {
                                    logger.Error(cloudEx, $"Cloud upload failed for game '{game.Name}'");
                                    
                                    // 显示本地备份成功的通知（云上传失败）
                                    PlayniteApi.Notifications.Add(new NotificationMessage(
                                        $"SaveManager_RealtimeSync_{game.Id}",
                                        ResourceProvider.GetString("LOCSaveManagerMsgRealtimeSyncCreated"),
                                        NotificationType.Info));
                                }
                            });
                        }
                        else
                        {
                            // 未启用云同步，直接显示本地通知
                            PlayniteApi.Notifications.Add(new NotificationMessage(
                                $"SaveManager_RealtimeSync_{game.Id}",
                                ResourceProvider.GetString("LOCSaveManagerMsgRealtimeSyncCreated"),
                                NotificationType.Info));

                            // 显示 Windows Toast 通知
                            ToastNotificationService.ShowBackupSuccess(game.Name, "Latest.zip", game.Icon);
                        }
                    }
                    catch (Exception syncEx)
                    {
                        logger.Error(syncEx, $"Realtime sync snapshot failed for game '{game.Name}'");
                        
                        // 显示错误通知
                        PlayniteApi.Notifications.Add(new NotificationMessage(
                            $"SaveManager_RealtimeSyncError_{game.Id}",
                            $"Real-time sync failed: {syncEx.Message}",
                            NotificationType.Error));

                        // 弹窗提示错误
                        PlayniteApi.Dialogs.ShowErrorMessage(
                            $"Real-time sync failed for {game.Name}:\n{syncEx.Message}",
                            "Real-time Sync Error");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"OnGameStopped failed for game '{game.Name}'");
            }
        }

        /// <summary>
        /// 导出全局配置
        /// </summary>
        public void ExportGlobalConfig()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = ResourceProvider.GetString("LOCSaveManagerMenuExportGlobalConfig"),
                    Filter = "ZIP Archive (*.zip)|*.zip",
                    FileName = $"SaveManager_GlobalConfig_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
                };

                var window = PlayniteApi.Dialogs.GetCurrentAppWindow();
                if (dialog.ShowDialog(window) == true)
                {
                    var dataPath = GetPluginUserDataPath();
                    
                    // 创建ZIP文件
                    if (System.IO.File.Exists(dialog.FileName))
                    {
                        System.IO.File.Delete(dialog.FileName);
                    }
                    
                    System.IO.Compression.ZipFile.CreateFromDirectory(dataPath, dialog.FileName);
                    
                    PlayniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerGlobalExportSuccess"), dialog.FileName),
                        "Save Manager",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to export global config");
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 导入全局配置
        /// </summary>
        public void ImportGlobalConfig()
        {
            try
            {
                // 显示警告
                var warningResult = PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCSaveManagerGlobalImportWarning"),
                    ResourceProvider.GetString("LOCSaveManagerGlobalImportTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (warningResult != MessageBoxResult.Yes)
                {
                    return;
                }

                var path = PlayniteApi.Dialogs.SelectFile("ZIP Archive (*.zip)|*.zip");
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                var dataPath = GetPluginUserDataPath();

                // 备份当前配置
                var backupPath = dataPath + "_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                if (System.IO.Directory.Exists(dataPath))
                {
                    System.IO.Directory.Move(dataPath, backupPath);
                }

                // 解压导入的配置
                System.IO.Directory.CreateDirectory(dataPath);
                System.IO.Compression.ZipFile.ExtractToDirectory(path, dataPath);

                // 删除备份（导入成功后）
                if (System.IO.Directory.Exists(backupPath))
                {
                    System.IO.Directory.Delete(backupPath, true);
                }

                // 重新加载 BackupService 以读取新数据
                backupService = new BackupService(dataPath, logger, PlayniteApi, () => settings.RealtimeSyncEnabled);

                PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCSaveManagerGlobalImportSuccess"),
                    "Save Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // 导入成功后，自动弹出游戏匹配向导（缩减版，只显示未匹配的）
                var configs = backupService.GetAllGameConfigs();
                if (configs.Count > 0)
                {
                    var matchResult = PlayniteApi.Dialogs.ShowMessage(
                        ResourceProvider.GetString("LOCSaveManagerGameMatchingPrompt"),
                        "Save Manager",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (matchResult == MessageBoxResult.Yes)
                    {
                        OpenGameMatchingWindow(fullMode: false);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to import global config");
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 打开游戏匹配窗口
        /// </summary>
        /// <param name="fullMode">是否为完整模式（显示所有配置）</param>
        public void OpenGameMatchingWindow(bool fullMode)
        {
            try
            {
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false
                });

                window.Width = 750;
                window.Height = 550;
                window.Title = fullMode 
                    ? ResourceProvider.GetString("LOCSaveManagerGameMatchingTitleFull")
                    : ResourceProvider.GetString("LOCSaveManagerGameMatchingTitleSimple");
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();

                var viewModel = new GameMatchingViewModel(PlayniteApi, backupService, fullMode);
                var view = new GameMatchingView
                {
                    DataContext = viewModel
                };

                // 处理关闭事件
                viewModel.RequestClose += (result) =>
                {
                    window.DialogResult = result;
                    window.Close();
                };

                window.Content = view;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to open game matching window");
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 删除所有插件数据（配置、备份、设置）
        /// </summary>
        public void DeleteAllPluginData()
        {
            var dataPath = GetPluginUserDataPath();
            
            if (Directory.Exists(dataPath))
            {
                // 先备份 rclone.exe（如果存在）
                var rclonePath = Path.Combine(dataPath, "rclone.exe");
                var tempRclonePath = Path.Combine(Path.GetTempPath(), "rclone_backup.exe");
                bool rcloneExists = File.Exists(rclonePath);
                
                if (rcloneExists)
                {
                    File.Copy(rclonePath, tempRclonePath, overwrite: true);
                    logger.Info("Backed up rclone.exe before deletion");
                }
                
                // 删除整个数据目录
                Directory.Delete(dataPath, recursive: true);
                logger.Info("Deleted all plugin data");
                
                // 重新创建数据目录
                Directory.CreateDirectory(dataPath);
                
                // 恢复 rclone.exe
                if (rcloneExists && File.Exists(tempRclonePath))
                {
                    File.Move(tempRclonePath, rclonePath);
                    logger.Info("Restored rclone.exe after deletion");
                }
                
                // 重新初始化服务
                backupService = new BackupService(dataPath, logger, PlayniteApi, () => settings.RealtimeSyncEnabled);
                
                // 重置设置为默认值
                settings = new SaveManagerSettings(this);
                
                logger.Info("Re-initialized plugin after data deletion");
            }
        }
    }
}
