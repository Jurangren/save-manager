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
using System.Threading.Tasks;

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
        private Services.BackgroundTaskManager backgroundTaskManager;

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

        /// <summary>
        /// 获取备份服务实例
        /// </summary>
        public BackupService GetBackupService() => backupService;

        public SaveManagerPlugin(IPlayniteAPI api) : base(api)
        {
            // 初始化设置
            settings = new SaveManagerSettings(this);
            
            // 初始化服务
            var dataPath = GetPluginUserDataPath();
            backupService = new BackupService(dataPath, logger, PlayniteApi, () => settings.RealtimeSyncEnabled);

            // 初始化 Rclone 服务
            rcloneService = new Services.RcloneService(dataPath, logger, PlayniteApi);

            // 初始化后台任务管理器
            backgroundTaskManager = new Services.BackgroundTaskManager();

            // 初始化云同步管理器
            cloudSyncManager = new Services.CloudSyncManager(dataPath, backupService, rcloneService, PlayniteApi, logger);
            cloudSyncManager.GetCloudProvider = () => (Models.CloudProvider)settings.CloudProvider;
            cloudSyncManager.GetCloudSyncEnabled = () => settings.CloudSyncEnabled;

            // 设置属性以启用设置视图
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            // 监听进程退出，确保后台任务完成
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            logger.Info("Save Manager plugin initialized");
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            if (backgroundTaskManager != null && backgroundTaskManager.HasActiveTasks)
            {
                logger.Info($"Process exiting, waiting for {backgroundTaskManager.ActiveTaskCount} background tasks...");
                
                // 此时 UI 线程可能已不可用，只能盲等
                var timeout = DateTime.Now.AddSeconds(60);
                while (backgroundTaskManager.HasActiveTasks && DateTime.Now < timeout)
                {
                    System.Threading.Thread.Sleep(500);
                }
                
                logger.Info("Background wait finished.");
            }
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
                        // 设置完整路径以便检查本地文件是否存在
                        backup.FullPath = backupService.GetFullBackupPath(backup.BackupFilePath);
                        var displayText = string.IsNullOrEmpty(backup.Description) 
                            ? backup.Name 
                            : backup.Description;
                        var subText = backup.FormattedDate;
                        // 云存档在名称前添加云朵图标
                        var isLocal = backup.IsLocalFileExists;
                        var cloudPrefix = isLocal ? "" : "☁️ ";
                        
                        yield return new GameMenuItem
                        {
                            Description = $"{cloudPrefix}{displayText}  ({subText})",
                            MenuSection = restoreMenuSection,
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
                window.Title = ResourceProvider.GetString("LOCSaveManagerSubtitle");
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();

                var viewModel = new SaveManagerViewModel(game, PlayniteApi, backupService, cloudSyncManager, rcloneService,
                    () => settings.CloudSyncEnabled, () => settings.RealtimeSyncEnabled);
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

                SaveBackup backup = null;
                
                // 在创建备份前，检查本地是否已有 Latest 备份
                bool hadLatestBefore = false;
                var existingBackups = backupService.GetBackupsByConfigId(config.ConfigId);
                hadLatestBefore = existingBackups.Any(b => b.Name == "Latest");

                // 使用进度窗口创建备份
                PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgCreatingBackup");
                    progressArgs.IsIndeterminate = true;
                    backup = backupService.CreateBackup(game.Id, game.Name, noteResult.SelectedString);
                }, new GlobalProgressOptions(
                    ResourceProvider.GetString("LOCSaveManagerMsgCreatingBackup"), false)
                {
                    IsIndeterminate = true
                });

                if (backup == null)
                {
                    return;
                }

                // 如果启用了云同步
                if (settings.CloudSyncEnabled && cloudSyncManager != null)
                {
                    // 启动后台同步
                    RunBackgroundCloudSync(backup, game.Name, true);

                    PlayniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgBackupSuccess") + "\n(Cloud sync will continue in background)", backup.Name, backup.FormattedSize),
                        "Save Manager",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // 如果之前没有 Latest 备份，现在有了（实时同步启用时会创建），需要推送 Latest
                    if (!hadLatestBefore && settings.RealtimeSyncEnabled)
                    {
                        // 刷新配置，获取新创建的 Latest 备份
                        config = backupService.GetGameConfig(game.Id);
                        if (config != null)
                        {
                            var latestBackup = backupService.GetBackupsByConfigId(config.ConfigId)
                                .FirstOrDefault(b => b.Name == "Latest");
                            
                            if (latestBackup != null)
                            {
                                // 后台上传 Latest
                                RunBackgroundCloudSync(latestBackup, game.Name, true);
                            }
                        }
                    }
                }
                else
                {
                    PlayniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgBackupSuccess"), backup.Name, backup.FormattedSize),
                        "Save Manager",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Quick backup failed for game {game.Name}");
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 同步备份到云端，带后台同步选项（右键菜单用）
        /// </summary>
        private void SyncBackupToCloudWithBackgroundOption(SaveBackup backup, string gameName)
        {
            bool success = false;
            bool useBackground = false;

            // 启动后台上传任务
            var uploadTask = System.Threading.Tasks.Task.Run(async () =>
            {
                return await cloudSyncManager.UploadBackupToCloudAsync(backup, gameName);
            });

            // 显示进度窗口，带取消（后台同步）按钮
            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                progressArgs.IsIndeterminate = true;
                progressArgs.Text = string.Format(ResourceProvider.GetString("LOCSaveManagerMsgUploadingToCloud"), backup.Name);

                // 等待任务完成或用户取消
                while (!uploadTask.IsCompleted && !progressArgs.CancelToken.IsCancellationRequested)
                {
                    System.Threading.Thread.Sleep(100);
                }

                if (progressArgs.CancelToken.IsCancellationRequested)
                {
                    // 用户点击了"后台同步"按钮
                    useBackground = true;
                }
                else
                {
                    success = uploadTask.Result;
                }
            }, new GlobalProgressOptions(
                ResourceProvider.GetString("LOCSaveManagerMsgSyncingToCloud"), true)
            {
                IsIndeterminate = true
            });

            if (useBackground)
            {
                // 后台继续上传，完成后通知
                uploadTask.ContinueWith(t =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (t.IsCompleted && !t.IsFaulted && t.Result)
                        {
                            PlayniteApi.Notifications.Add(new NotificationMessage(
                                $"SaveManager_CloudSync_{backup.Name}_{DateTime.Now.Ticks}",
                                string.Format(ResourceProvider.GetString("LOCSaveManagerMsgBackupUploadComplete"), backup.Name),
                                NotificationType.Info));
                        }
                        else
                        {
                            PlayniteApi.Notifications.Add(new NotificationMessage(
                                $"SaveManager_CloudSync_Error_{backup.Name}",
                                string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncFailed"), backup.Name),
                                NotificationType.Error));
                        }
                    });
                });
            }
            else if (!success)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncFailed"), backup.Name),
                    "Cloud Sync Error");
            }
        }

        /// <summary>
        /// 前台同步 Latest 到云端（使用进度对话框）
        /// </summary>
        private void SyncLatestToCloudForeground(SaveBackup latestBackup, string gameName)
        {
            bool success = false;

            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                progressArgs.IsIndeterminate = true;
                progressArgs.Text = string.Format(ResourceProvider.GetString("LOCSaveManagerMsgUploadingToCloud"), latestBackup.Name);

                try
                {
                    var task = cloudSyncManager.UploadBackupToCloudAsync(latestBackup, gameName);
                    task.Wait();
                    success = task.Result;
                }
                catch { }
            }, new GlobalProgressOptions(
                ResourceProvider.GetString("LOCSaveManagerMsgSyncingToCloud"), false)
            {
                IsIndeterminate = true
            });

            if (!success)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncFailed"), latestBackup.Name),
                    "Cloud Sync Error");
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

                    // 使用进度窗口还原备份
                    PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                    {
                        progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgRestoringBackup");
                        progressArgs.IsIndeterminate = true;
                        backupService.RestoreBackup(latestBackup, excludePaths);
                    }, new GlobalProgressOptions(
                        ResourceProvider.GetString("LOCSaveManagerMsgRestoringBackup"), false)
                    {
                        IsIndeterminate = true
                    });

                    // 还原后，自动更新 Latest (仅当实时同步启用时)
                    if (settings.RealtimeSyncEnabled)
                    {
                        bool cloudEnabled = settings.CloudSyncEnabled;
                        string progressText = cloudEnabled 
                            ? ResourceProvider.GetString("LOCSaveManagerMsgUpdatingLatest")
                            : ResourceProvider.GetString("LOCSaveManagerMsgUpdatingLatestLocal");

                        PlayniteApi.Dialogs.ActivateGlobalProgress((localArgs) =>
                        {
                            localArgs.Text = progressText;
                            localArgs.IsIndeterminate = true;
                            
                            try
                            {
                                // 使用被还原备份的历史记录
                                var newLatest = backupService.CreateRealtimeSyncSnapshot(game.Id, game.Name, latestBackup.VersionHistory);
                                
                                if (cloudEnabled && cloudSyncManager != null)
                                {
                                    // 后台上传 Latest
                                    RunBackgroundCloudSync(newLatest, game.Name, true);
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Error(ex, "Error Updating Latest");
                                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error Updating Latest");
                            }

                        }, new GlobalProgressOptions(progressText, false) { IsIndeterminate = true });
                    }

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
                    // 检查本地文件是否存在
                    if (!backup.IsLocalFileExists)
                    {
                        // 需要先从云端下载
                        if (!DownloadBackupFromCloud(backup))
                        {
                            return; // 下载失败
                        }
                    }

                    // 获取排除项配置
                    var config = backupService.GetGameConfig(game.Id);
                    var excludePaths = config?.RestoreExcludePaths;

                    // 使用进度窗口还原备份
                    PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                    {
                        progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgRestoringBackup");
                        progressArgs.IsIndeterminate = true;
                        backupService.RestoreBackup(backup, excludePaths);
                    }, new GlobalProgressOptions(
                        ResourceProvider.GetString("LOCSaveManagerMsgRestoringBackup"), false)
                    {
                        IsIndeterminate = true
                    });

                    // 还原后，自动更新 Latest (仅当实时同步启用时)
                    if (settings.RealtimeSyncEnabled)
                    {
                        bool cloudEnabled = settings.CloudSyncEnabled;
                        string progressText = cloudEnabled 
                            ? ResourceProvider.GetString("LOCSaveManagerMsgUpdatingLatest")
                            : ResourceProvider.GetString("LOCSaveManagerMsgUpdatingLatestLocal");

                        PlayniteApi.Dialogs.ActivateGlobalProgress((localArgs) =>
                        {
                            localArgs.Text = progressText;
                            localArgs.IsIndeterminate = true;
                            
                            try
                            {
                                // 使用被还原备份的历史记录
                                var newLatest = backupService.CreateRealtimeSyncSnapshot(game.Id, game.Name, backup.VersionHistory);
                                
                                if (cloudEnabled && cloudSyncManager != null)
                                {
                                    // 后台上传 Latest
                                    RunBackgroundCloudSync(newLatest, game.Name, true);
                                }
                            }
                            catch (Exception ex)
                            {
                                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error Updating Latest");
                            }

                        }, new GlobalProgressOptions(progressText, false) { IsIndeterminate = true });
                    }

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
        /// 从云端下载备份文件（右键菜单用）
        /// </summary>
        private bool DownloadBackupFromCloud(SaveBackup backup)
        {
            if (!settings.CloudSyncEnabled)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncNotEnabled"),
                    "Error");
                return false;
            }

            if (rcloneService == null || !rcloneService.IsRcloneInstalled)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgRcloneNotInstalled"),
                    "Error");
                return false;
            }

            var config = backupService.GetConfigByConfigId(backup.ConfigId);
            if (config == null)
            {
                PlayniteApi.Dialogs.ShowErrorMessage("Config not found", "Error");
                return false;
            }

            bool downloaded = false;

            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgDownloadingBackup");
                progressArgs.IsIndeterminate = true;

                try
                {
                    var provider = (Models.CloudProvider)settings.CloudProvider;
                    var remoteGamePath = rcloneService.GetRemoteGamePath(config.ConfigId, config.GameName);
                    var remoteBackupPath = $"{remoteGamePath}/{backup.Name}.zip";
                    var localBackupPath = backupService.GetFullBackupPath(backup.BackupFilePath);

                    // 确保本地目录存在
                    var localDir = System.IO.Path.GetDirectoryName(localBackupPath);
                    if (!System.IO.Directory.Exists(localDir))
                    {
                        System.IO.Directory.CreateDirectory(localDir);
                    }

                    var task = rcloneService.DownloadFileAsync(remoteBackupPath, localBackupPath, provider);
                    task.Wait();
                    downloaded = task.Result;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Download backup from cloud failed");
                }
            }, new GlobalProgressOptions(
                ResourceProvider.GetString("LOCSaveManagerMsgDownloadingBackup"), false)
            {
                IsIndeterminate = true
            });

            if (!downloaded)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgDownloadFailed"),
                    "Error");
            }

            return downloaded;
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
        /// 应用停止时触发 - 等待所有后台任务完成
        /// </summary>
        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            if (backgroundTaskManager == null || !backgroundTaskManager.HasActiveTasks)
            {
                logger.Info("No background tasks running, exiting immediately.");
                return;
            }

            var taskCount = backgroundTaskManager.ActiveTaskCount;
            logger.Info($"Waiting for {taskCount} background tasks to complete before exit...");

            // 显示进度对话框等待任务完成
            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                progressArgs.IsIndeterminate = true;
                progressArgs.Text = string.Format(
                    ResourceProvider.GetString("LOCSaveManagerMsgWaitingForTasks"),
                    backgroundTaskManager.ActiveTaskCount);

                // 等待所有任务完成，最多等待5分钟
                var completed = backgroundTaskManager.WaitForAllTasks(TimeSpan.FromMinutes(5));
                
                if (!completed)
                {
                    logger.Warn("Timeout waiting for background tasks, some tasks may be incomplete.");
                }
            }, new GlobalProgressOptions(
                ResourceProvider.GetString("LOCSaveManagerMsgWaitingForTasks"), false)
            {
                IsIndeterminate = true
            });

            logger.Info("All background tasks completed, proceeding with exit.");
        }

        /// <summary>
        /// 应用启动时触发 - 从云端同步 config.json（强制等待）
        /// </summary>
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (settings.CloudSyncEnabled)
            {
                SyncConfigWithRetry();
            }
        }

        /// <summary>
        /// 同步配置，失败时提供重试选项
        /// </summary>
        private void SyncConfigWithRetry()
        {
            bool shouldRetry = true;
            
            while (shouldRetry)
            {
                shouldRetry = false;
                Services.CloudSyncManager.ConfigSyncResult syncResult = null;

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
                                syncResult = new Services.CloudSyncManager.ConfigSyncResult
                                {
                                    Success = false,
                                    ErrorMessage = task.Exception?.InnerException?.Message ?? task.Exception?.Message
                                };
                            }
                            else
                            {
                                syncResult = task.Result;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Cloud sync on application started failed");
                            syncResult = new Services.CloudSyncManager.ConfigSyncResult
                            {
                                Success = false,
                                ErrorMessage = ex.Message
                            };
                        }
                    }, new GlobalProgressOptions(
                        ResourceProvider.GetString("LOCSaveManagerMsgSyncingConfig"), false)
                    {
                        IsIndeterminate = true
                    });

                    // 处理同步结果
                    if (syncResult != null)
                    {
                        if (!syncResult.Success)
                        {
                            // 同步失败，显示重试/忽略对话框
                            var options = new List<MessageBoxOption>
                            {
                                new MessageBoxOption(
                                    ResourceProvider.GetString("LOCSaveManagerBtnRetry"), true, false),
                                new MessageBoxOption(
                                    ResourceProvider.GetString("LOCSaveManagerBtnIgnore"), false, true)
                            };

                            var selectedOption = PlayniteApi.Dialogs.ShowMessage(
                                string.Format(ResourceProvider.GetString("LOCSaveManagerMsgConfigSyncFailed"), syncResult.ErrorMessage),
                                "Save Manager - Cloud Sync Error",
                                MessageBoxImage.Error,
                                options);

                            if (selectedOption == options[0])
                            {
                                // 用户选择重试
                                shouldRetry = true;
                            }
                            // 用户选择忽略，退出循环
                        }
                        else if (syncResult.NewConfigIds.Count > 0)
                        {
                            // 有新配置，显示游戏匹配对话框
                            ShowGameMatchingDialogForNewConfigs(syncResult.NewConfigIds);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Cloud sync on application started failed");
                    
                    // 显示重试/忽略对话框
                    var options = new List<MessageBoxOption>
                    {
                        new MessageBoxOption(
                            ResourceProvider.GetString("LOCSaveManagerBtnRetry"), true, false),
                        new MessageBoxOption(
                            ResourceProvider.GetString("LOCSaveManagerBtnIgnore"), false, true)
                    };

                    var selectedOption = PlayniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgConfigSyncFailed"), ex.Message),
                        "Save Manager - Cloud Sync Error",
                        MessageBoxImage.Error,
                        options);

                    if (selectedOption == options[0])
                    {
                        // 用户选择重试
                        shouldRetry = true;
                    }
                }
            }
        }

        /// <summary>
        /// 显示游戏匹配对话框（仅显示新增的配置）
        /// </summary>
        private void ShowGameMatchingDialogForNewConfigs(List<Guid> newConfigIds)
        {
            try
            {
                // 创建 GameMatchingViewModel，传入新配置ID列表
                var viewModel = new ViewModels.GameMatchingViewModel(PlayniteApi, backupService, newConfigIds, cloudSyncManager, () => settings.CloudSyncEnabled);
                
                // 如果所有新配置都已自动匹配，不需要显示对话框
                if (viewModel.UnmatchedCount == 0 && !viewModel.MatchingItems.Any())
                {
                    logger.Info("All new configs auto-matched, no dialog needed");
                    return;
                }

                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true
                });

                window.Height = 600;
                window.Width = 900;
                window.Title = ResourceProvider.GetString("LOCSaveManagerTitleGameMatchingNew");
                window.Content = new Views.GameMatchingView { DataContext = viewModel };
                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                viewModel.CloseAction = () => window.Close();
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to show game matching dialog for new configs");
            }
        }

        /// <summary>
        /// 游戏启动前同步配置文件（带进度对话框）
        /// </summary>
        private void SyncConfigBeforeGameStart()
        {
            Services.CloudSyncManager.ConfigSyncResult syncResult = null;

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
                        logger.Error(task.Exception, "Failed to sync config before game start");
                        syncResult = new Services.CloudSyncManager.ConfigSyncResult
                        {
                            Success = false,
                            ErrorMessage = task.Exception?.InnerException?.Message ?? task.Exception?.Message
                        };
                    }
                    else
                    {
                        syncResult = task.Result;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Sync config before game start failed");
                    syncResult = new Services.CloudSyncManager.ConfigSyncResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message
                    };
                }
            }, new GlobalProgressOptions(
                ResourceProvider.GetString("LOCSaveManagerMsgSyncingConfig"), false)
            {
                IsIndeterminate = true
            });

            // 如果同步失败，记录日志但不阻止游戏启动
            if (syncResult != null && !syncResult.Success)
            {
                logger.Warn($"Config sync before game start failed: {syncResult.ErrorMessage}");
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
                // 如果启用了游戏启动时同步配置，先同步config.json
                if (settings.SyncConfigOnGameStart)
                {
                    SyncConfigBeforeGameStart();
                }

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
                        // 使用实际本地文件修改时间
                        var localTime = syncInfo.LocalFileModifiedTime ?? DateTime.MinValue;
                        var localDate = localTime > DateTime.MinValue 
                            ? localTime.ToString("yyyy-MM-dd HH:mm:ss") 
                            : "Unknown";
                        var localSize = syncInfo.LocalBackup?.FormattedSize ?? "Unknown";
                        
                        // 云端信息: 如果时间是 MinValue 说明没有记录
                        var cloudTime = syncInfo.CloudBackup?.CreatedAt ?? DateTime.MinValue;
                        var cloudDate = cloudTime > DateTime.MinValue 
                            ? cloudTime.ToString("yyyy-MM-dd HH:mm:ss") 
                            : "Unknown";
                        var cloudSize = (syncInfo.CloudBackup?.FileSize > 0) 
                            ? syncInfo.CloudBackup.FormattedSize 
                            : "Unknown";
                        
                        // 比较时间，确定哪个更新
                        bool localIsNewer = localTime > cloudTime && localTime > DateTime.MinValue;
                        bool cloudIsNewer = cloudTime > localTime && cloudTime > DateTime.MinValue;
                        
                        // 添加"（时间最新）"标记
                        string localDateDisplay = localDate + (localIsNewer ? ResourceProvider.GetString("LOCSaveManagerMsgNewest") : "");
                        string cloudDateDisplay = cloudDate + (cloudIsNewer ? ResourceProvider.GetString("LOCSaveManagerMsgNewest") : "");
                        
                        // 按钮文本，给最新的添加"(建议)"
                        string pullCloudText = ResourceProvider.GetString("LOCSaveManagerBtnPullCloud") 
                            + (cloudIsNewer ? ResourceProvider.GetString("LOCSaveManagerMsgRecommended") : "");
                        string keepLocalText = ResourceProvider.GetString("LOCSaveManagerBtnKeepLocalAndPush") 
                            + (localIsNewer ? ResourceProvider.GetString("LOCSaveManagerMsgRecommended") : "");
                        
                        // 三按钮对话框：拉取云端、保留本地并推送、取消
                        // 默认按钮设为"取消"
                        var conflictOptions = new List<Playnite.SDK.MessageBoxOption>
                        {
                            new Playnite.SDK.MessageBoxOption(pullCloudText, false, false),
                            new Playnite.SDK.MessageBoxOption(keepLocalText, false, false),
                            new Playnite.SDK.MessageBoxOption(ResourceProvider.GetString("LOCSaveManagerBtnCancel"), true, true)
                        };

                        // 构建冲突消息，使用 Environment.NewLine 确保换行
                        var conflictMessage = string.Format(
                            ResourceProvider.GetString("LOCSaveManagerMsgSaveConflictOptions"),
                            localDateDisplay,
                            localSize,
                            cloudDateDisplay,
                            cloudSize);
                        // 替换可能的换行符编码
                        conflictMessage = conflictMessage.Replace("&#x0a;", Environment.NewLine)
                                                         .Replace("\\n", Environment.NewLine);

                        var conflictResult = PlayniteApi.Dialogs.ShowMessage(
                            conflictMessage,
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
                        else
                        {
                            // 用户点击取消：不启动游戏
                            args.CancelStartup = true;
                            logger.Info($"User cancelled game start due to save conflict for '{game.Name}'");
                        }
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
                        // 清理超出数量限制的旧自动备份
                        var deletedBackups = backupService.CleanupOldAutoBackups(game.Id, settings.MaxAutoBackupCount);

                        // 如果启用了云同步，也从云端删除这些备份
                        if (settings.CloudSyncEnabled && cloudSyncManager != null && deletedBackups.Count > 0)
                        {
                            foreach (var deletedBackup in deletedBackups)
                            {
                                var deletedBackupName = deletedBackup.Name;
                                var gameNameForDelete = game.Name;
                                // 使用后台任务管理器跟踪删除任务
                                backgroundTaskManager.RunTask($"AutoBackupDelete_{gameNameForDelete}_{deletedBackupName}", async () =>
                                {
                                    try 
                                    {
                                        var success = await cloudSyncManager.DeleteBackupFromCloudAsync(deletedBackup, gameNameForDelete);
                                        if (success)
                                        {
                                            logger.Info($"Old auto-backup deleted from cloud for game '{gameNameForDelete}': {deletedBackupName}");
                                        }
                                        else
                                        {
                                            logger.Warn($"Failed to delete old auto-backup from cloud for game '{gameNameForDelete}': {deletedBackupName}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.Error(ex, $"Error deleting old auto-backup from cloud for game '{gameNameForDelete}': {deletedBackupName}");
                                    }
                                });
                            }
                        }

                        // 如果启用了云同步，上传自动备份到云端
                        if (settings.CloudSyncEnabled && cloudSyncManager != null)
                        {
                            // 使用后台任务管理器跟踪上传任务
                            var backupName = backup.Name;
                            var gameName = game.Name;
                            var gameId = game.Id;
                            var gameIcon = game.Icon;
                            
                            backgroundTaskManager.RunTask($"AutoBackupUpload_{gameName}", async () =>
                            {
                                try
                                {
                                    var success = await cloudSyncManager.UploadBackupToCloudAsync(backup, gameName);
                                    if (success)
                                    {
                                        logger.Info($"Auto backup uploaded to cloud for game '{gameName}': {backupName}");
                                        
                                        // 云上传成功后显示通知
                                        PlayniteApi.Notifications.Add(new NotificationMessage(
                                            $"SaveManager_AutoBackup_{gameId}",
                                            string.Format(ResourceProvider.GetString("LOCSaveManagerAutoBackupCloudSuccess"), gameName, backupName),
                                            NotificationType.Info));

                                        // 显示 Windows Toast 通知
                                        if (settings.ShowAutoBackupNotification)
                                        {
                                            ToastNotificationService.ShowBackupSuccess(gameName, $"{backupName} (Cloud)", gameIcon);
                                        }
                                    }
                                    else
                                    {
                                        logger.Warn($"Failed to upload auto backup to cloud for game '{gameName}'");
                                        
                                        // 本地备份成功但云上传失败
                                        PlayniteApi.Notifications.Add(new NotificationMessage(
                                            $"SaveManager_AutoBackup_{gameId}",
                                            string.Format(ResourceProvider.GetString("LOCSaveManagerAutoBackupSuccess"), gameName, backupName),
                                            NotificationType.Info));

                                        // 显示 Windows Toast 通知（仅本地）
                                        if (settings.ShowAutoBackupNotification)
                                        {
                                            ToastNotificationService.ShowBackupSuccess(gameName, backupName, gameIcon);
                                        }
                                    }
                                }
                                catch (Exception cloudEx)
                                {
                                    logger.Error(cloudEx, $"Cloud upload failed for auto backup '{gameName}'");
                                    
                                    // 本地备份成功但云上传失败
                                    PlayniteApi.Notifications.Add(new NotificationMessage(
                                        $"SaveManager_AutoBackup_{gameId}",
                                        string.Format(ResourceProvider.GetString("LOCSaveManagerAutoBackupSuccess"), gameName, backupName),
                                        NotificationType.Info));

                                    // 显示 Windows Toast 通知（仅本地）
                                    if (settings.ShowAutoBackupNotification)
                                    {
                                        ToastNotificationService.ShowBackupSuccess(gameName, backupName, gameIcon);
                                    }
                                }
                            });
                        }
                        else
                        {
                            // 未启用云同步，直接显示本地备份成功通知
                            PlayniteApi.Notifications.Add(new NotificationMessage(
                                $"SaveManager_AutoBackup_{game.Id}",
                                string.Format(ResourceProvider.GetString("LOCSaveManagerAutoBackupSuccess"), game.Name, backup.Name),
                                NotificationType.Info));

                            // 显示 Windows Toast 通知
                            if (settings.ShowAutoBackupNotification)
                            {
                                ToastNotificationService.ShowBackupSuccess(game.Name, backup.Name, game.Icon);
                            }
                        }
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
                            // 使用后台任务管理器跟踪上传任务
                            var gameName = game.Name;
                            var gameId = game.Id;
                            var gameIcon = game.Icon;
                            
                            backgroundTaskManager.RunTask($"LatestUpload_{gameName}", async () =>
                            {
                                try
                                {
                                    var success = await cloudSyncManager.UploadBackupToCloudAsync(syncBackup, gameName);
                                    if (success)
                                    {
                                        logger.Info($"Latest.zip uploaded to cloud for game '{gameName}'");
                                    }
                                    else
                                    {
                                        logger.Warn($"Failed to upload Latest.zip to cloud for game '{gameName}'");
                                    }
                                }
                                catch (Exception cloudEx)
                                {
                                    logger.Error(cloudEx, $"Cloud upload failed for game '{gameName}'");
                                }
                            });
                        }
                        else
                        {
                            // 未启用云同步，静默完成
                            logger.Info($"Realtime sync snapshot created locally for game '{game.Name}'");
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

                var viewModel = new GameMatchingViewModel(PlayniteApi, backupService, fullMode, cloudSyncManager, () => settings.CloudSyncEnabled);
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

        /// <summary>
        /// 后台运行云同步任务（上传或删除）
        /// </summary>
        private void RunBackgroundCloudSync(SaveBackup backup, string gameName, bool isUpload)
        {
            backgroundTaskManager.RunTask($"CloudSync_{backup.Name}", async () =>
            {
                try
                {
                    bool success = isUpload 
                        ? await cloudSyncManager.UploadBackupToCloudAsync(backup, gameName)
                        : await cloudSyncManager.DeleteBackupFromCloudAsync(backup);

                    if (success)
                    {
                        var message = isUpload
                            ? string.Format(ResourceProvider.GetString("LOCSaveManagerMsgBackupUploadComplete"), backup.Name)
                            : string.Format(ResourceProvider.GetString("LOCSaveManagerMsgBackupDeleteComplete"), backup.Name);
                        
                        PlayniteApi.Notifications.Add(new NotificationMessage(
                            $"SaveManager_CloudSync_{backup.Name}_{DateTime.Now.Ticks}",
                            message,
                            NotificationType.Info));
                    }
                    else
                    {
                         var message = string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncFailed"), backup.Name);
                         PlayniteApi.Notifications.Add(new NotificationMessage(
                            $"SaveManager_CloudSync_Error_{backup.Name}",
                            message,
                            NotificationType.Error));
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Background cloud sync failed");
                }
            });
        }
    }
}
