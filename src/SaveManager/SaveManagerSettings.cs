using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace SaveManager
{
    /// <summary>
    /// 插件设置类
    /// 注意：使用自定义的 settings.json 文件保存设置，
    /// 而不是 Playnite SDK 的 SavePluginSettings 方法，
    /// 因为后者会覆盖 config.json 导致游戏配置丢失。
    /// </summary>
    public class SaveManagerSettings : ObservableObject, ISettings
    {
        private readonly SaveManagerPlugin plugin;
        private static readonly ILogger logger = LogManager.GetLogger();

        /// <summary>
        /// 设置文件名（使用独立的文件名，避免与 config.json 冲突）
        /// </summary>
        private const string SettingsFileName = "settings.json";

        // 备份设置
        private string customBackupPath = string.Empty;
        private bool autoBackupOnGameExit = false;
        private bool confirmBeforeBackup = true;
        private int maxBackupCount = 0;

        /// <summary>
        /// 自定义备份目录
        /// </summary>
        public string CustomBackupPath
        {
            get => customBackupPath;
            set => SetValue(ref customBackupPath, value);
        }

        /// <summary>
        /// 游戏结束时自动备份
        /// </summary>
        public bool AutoBackupOnGameExit
        {
            get => autoBackupOnGameExit;
            set => SetValue(ref autoBackupOnGameExit, value);
        }

        /// <summary>
        /// 备份前确认
        /// </summary>
        public bool ConfirmBeforeBackup
        {
            get => confirmBeforeBackup;
            set => SetValue(ref confirmBeforeBackup, value);
        }

        /// <summary>
        /// 自动备份最大数量（0 表示无限制，超过此数量会自动删除最旧的备份）
        /// </summary>
        public int MaxAutoBackupCount
        {
            get => maxBackupCount;
            set => SetValue(ref maxBackupCount, value);
        }

        private bool showAutoBackupNotification = true;

        /// <summary>
        /// 自动备份完成后发送 Windows 通知
        /// </summary>
        public bool ShowAutoBackupNotification
        {
            get => showAutoBackupNotification;
            set => SetValue(ref showAutoBackupNotification, value);
        }

        // 侧边栏按钮设置
        private bool showSidebarButton = true;

        /// <summary>
        /// 是否显示侧边栏按钮
        /// </summary>
        public bool ShowSidebarButton
        {
            get => showSidebarButton;
            set => SetValue(ref showSidebarButton, value);
        }

        // 实时同步设置（默认启用）
        private bool realtimeSyncEnabled = true;

        // 标志：是否正在加载设置（加载时不弹警告）
        [DontSerialize]
        private bool isLoading = false;

        /// <summary>
        /// 游戏前后实时同步游玩存档
        /// </summary>
        public bool RealtimeSyncEnabled
        {
            get => realtimeSyncEnabled;
            set
            {
                // 如果用户尝试从启用改为禁用（且不是加载阶段），弹出警告
                if (!isLoading && realtimeSyncEnabled && !value && plugin?.PlayniteApi != null)
                {
                    var result = plugin.PlayniteApi.Dialogs.ShowMessage(
                        ResourceProvider.GetString("LOCSaveManagerMsgDisableRealtimeSyncWarning"),
                        ResourceProvider.GetString("LOCSaveManagerTitleWarning"),
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);

                    // 如果用户选择"否"，则不修改设置
                    if (result == System.Windows.MessageBoxResult.No)
                    {
                        return;
                    }
                }

                SetValue(ref realtimeSyncEnabled, value);
            }
        }

        // 云同步设置
        private bool cloudSyncEnabled = false;
        private int cloudProvider = 0; // 0 = Google Drive, 1 = OneDrive

        /// <summary>
        /// 云同步是否启用
        /// </summary>
        public bool CloudSyncEnabled
        {
            get => cloudSyncEnabled;
            set => SetValue(ref cloudSyncEnabled, value);
        }

        /// <summary>
        /// 云服务商（0 = Google Drive, 1 = OneDrive）
        /// </summary>
        public int CloudProvider
        {
            get => cloudProvider;
            set => SetValue(ref cloudProvider, value);
        }

        private bool syncConfigOnGameStart = false;

        /// <summary>
        /// 每次游戏启动时同步配置
        /// </summary>
        public bool SyncConfigOnGameStart
        {
            get => syncConfigOnGameStart;
            set => SetValue(ref syncConfigOnGameStart, value);
        }

        // 云服务验证状态（不序列化）
        [DontSerialize]
        private string cloudVerifyStatus = "";
        [DontSerialize]
        private bool isVerifyingCloud = false;

        /// <summary>
        /// 云服务验证状态文本
        /// </summary>
        [DontSerialize]
        public string CloudVerifyStatus
        {
            get => cloudVerifyStatus;
            set => SetValue(ref cloudVerifyStatus, value);
        }

        /// <summary>
        /// 是否正在验证云服务
        /// </summary>
        [DontSerialize]
        public bool IsVerifyingCloud
        {
            get => isVerifyingCloud;
            set => SetValue(ref isVerifyingCloud, value);
        }


        // 用于存储备份的原始值
        [DontSerialize]
        private string editingCustomBackupPath;
        [DontSerialize]
        private bool editingAutoBackupOnGameExit;
        [DontSerialize]
        private bool editingConfirmBeforeBackup;
        [DontSerialize]
        private int editingMaxBackupCount;
        [DontSerialize]
        private bool editingRealtimeSyncEnabled;
        [DontSerialize]
        private bool editingShowSidebarButton;

        // 命令
        [DontSerialize]
        public ICommand BrowseBackupPathCommand { get; }
        [DontSerialize]
        public ICommand GlobalExportCommand { get; }
        [DontSerialize]
        public ICommand GlobalImportCommand { get; }
        [DontSerialize]
        public ICommand OpenDataFolderCommand { get; }
        [DontSerialize]
        public ICommand OpenGameMatchingCommand { get; }
        [DontSerialize]
        public ICommand DeleteAllDataCommand { get; }
        [DontSerialize]
        public ICommand DeleteCloudDataCommand { get; }
        [DontSerialize]
        public ICommand ConfigureCloudProviderCommand { get; }
        [DontSerialize]
        public ICommand PullAllDataCommand { get; }
        [DontSerialize]
        public ICommand PushAllDataCommand { get; }
        [DontSerialize]
        public ICommand VerifyCloudCommand { get; }
        [DontSerialize]
        public ICommand SyncConfigNowCommand { get; }
        [DontSerialize]
        public ICommand VerifyCloudBackupsCommand { get; }

        /// <summary>
        /// 无参构造函数（序列化需要）
        /// </summary>
        public SaveManagerSettings()
        {
        }

        /// <summary>
        /// 带插件引用的构造函数
        /// </summary>
        public SaveManagerSettings(SaveManagerPlugin plugin)
        {
            this.plugin = plugin;

            // 初始化命令
            BrowseBackupPathCommand = new Playnite.SDK.RelayCommand(() => BrowseBackupPath());
            GlobalExportCommand = new Playnite.SDK.RelayCommand(() => GlobalExport());
            GlobalImportCommand = new Playnite.SDK.RelayCommand(() => GlobalImport());
            OpenDataFolderCommand = new Playnite.SDK.RelayCommand(() => OpenDataFolder());
            OpenGameMatchingCommand = new Playnite.SDK.RelayCommand(() => OpenGameMatching());
            DeleteAllDataCommand = new Playnite.SDK.RelayCommand(() => DeleteAllData());
            DeleteCloudDataCommand = new Playnite.SDK.RelayCommand(async () => await DeleteCloudDataAsync());
            ConfigureCloudProviderCommand = new Playnite.SDK.RelayCommand(async () => await ConfigureCloudProviderAsync());
            PullAllDataCommand = new Playnite.SDK.RelayCommand(async () => await PullAllDataAsync());
            PushAllDataCommand = new Playnite.SDK.RelayCommand(async () => await PushAllDataAsync());
            VerifyCloudCommand = new Playnite.SDK.RelayCommand(() => VerifyCloud());
            SyncConfigNowCommand = new Playnite.SDK.RelayCommand(async () => await SyncConfigNowAsync());
            VerifyCloudBackupsCommand = new Playnite.SDK.RelayCommand(async () => await VerifyCloudBackupsAsync());

            // 加载保存的设置（使用自定义方法）
            LoadSettings();
        }

        /// <summary>
        /// 获取设置文件的完整路径
        /// </summary>
        private string GetSettingsFilePath()
        {
            return Path.Combine(plugin.GetPluginUserDataPath(), SettingsFileName);
        }

        /// <summary>
        /// 从自定义文件加载设置
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                isLoading = true; // 标记正在加载，避免触发警告弹窗
                
                var settingsPath = GetSettingsFilePath();
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var savedSettings = Serialization.FromJson<SaveManagerSettings>(json);
                    if (savedSettings != null)
                    {
                        CustomBackupPath = savedSettings.CustomBackupPath ?? string.Empty;
                        AutoBackupOnGameExit = savedSettings.AutoBackupOnGameExit;
                        ConfirmBeforeBackup = savedSettings.ConfirmBeforeBackup;
                        MaxAutoBackupCount = savedSettings.MaxAutoBackupCount;
                        ShowAutoBackupNotification = savedSettings.ShowAutoBackupNotification;
                        RealtimeSyncEnabled = savedSettings.RealtimeSyncEnabled;
                        SyncConfigOnGameStart = savedSettings.SyncConfigOnGameStart;
                        // 云同步设置
                        cloudSyncEnabled = savedSettings.CloudSyncEnabled;
                        cloudProvider = savedSettings.CloudProvider;
                        logger.Info($"Settings loaded: CloudSyncEnabled={cloudSyncEnabled}, CloudProvider={cloudProvider}, SyncConfigOnGameStart={syncConfigOnGameStart}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to load plugin settings from settings.json");
            }
            finally
            {
                isLoading = false; // 加载完成
            }
        }

        /// <summary>
        /// 保存设置到自定义文件
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                var settingsPath = GetSettingsFilePath();
                var json = Serialization.ToJson(this, true);
                File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to save plugin settings to settings.json");
            }
        }

        private void BrowseBackupPath()
        {
            var path = plugin?.PlayniteApi?.Dialogs?.SelectFolder();
            if (!string.IsNullOrEmpty(path))
            {
                CustomBackupPath = path;
            }
        }

        /// <summary>
        /// 全局导出（调用插件主类方法）
        /// </summary>
        private void GlobalExport()
        {
            plugin?.ExportGlobalConfig();
        }

        /// <summary>
        /// 全局导入（调用插件主类方法）
        /// </summary>
        private void GlobalImport()
        {
            plugin?.ImportGlobalConfig();
        }

        /// <summary>
        /// 打开数据文件夹
        /// </summary>
        private void OpenDataFolder()
        {
            try
            {
                var dataPath = plugin.GetPluginUserDataPath();
                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                }

                Process.Start("explorer.exe", dataPath);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to open data folder");
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 打开游戏匹配窗口（完整模式）
        /// </summary>
        private void OpenGameMatching()
        {
            plugin?.OpenGameMatchingWindow(fullMode: true);
        }

        /// <summary>
        /// 删除所有数据（配置和备份）
        /// </summary>
        private void DeleteAllData()
        {
            try
            {
                var result = plugin.PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgConfirmDeleteAllData"),
                    ResourceProvider.GetString("LOCSaveManagerTitleWarning"),
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    plugin?.DeleteAllPluginData();
                    
                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        ResourceProvider.GetString("LOCSaveManagerMsgDeleteAllDataSuccessRestart"),
                        "Save Manager",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to delete all data");
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                    $"{ResourceProvider.GetString("LOCSaveManagerMsgDeleteAllDataFailed")}\n{ex.Message}",
                    "Error");
            }
        }

        /// <summary>
        /// 删除云端所有数据
        /// </summary>
        private async System.Threading.Tasks.Task DeleteCloudDataAsync()
        {
            try
            {
                if (!CloudSyncEnabled)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        ResourceProvider.GetString("LOCSaveManagerMsgCloudNotEnabled"),
                        "Save Manager",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var result = plugin.PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgConfirmDeleteCloudData"),
                    ResourceProvider.GetString("LOCSaveManagerTitleWarning"),
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    var rcloneService = plugin.GetRcloneService();
                    if (rcloneService == null)
                    {
                        plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                            "Cloud sync service not initialized.",
                            "Error");
                        return;
                    }

                    bool success = false;
                    plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                    {
                        progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgDeletingCloudData");
                        progressArgs.IsIndeterminate = true;

                        var task = rcloneService.DeleteAllCloudDataAsync((Models.CloudProvider)CloudProvider);
                        task.Wait();
                        success = task.Result;
                    }, new GlobalProgressOptions(
                        ResourceProvider.GetString("LOCSaveManagerMsgDeletingCloudData"), false)
                    {
                        IsIndeterminate = true
                    });

                    if (success)
                    {
                        plugin.PlayniteApi.Dialogs.ShowMessage(
                            ResourceProvider.GetString("LOCSaveManagerMsgDeleteCloudDataSuccess"),
                            "Save Manager",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    }
                    else
                    {
                        plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                            ResourceProvider.GetString("LOCSaveManagerMsgDeleteCloudDataFailed"),
                            "Error");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to delete cloud data");
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                    $"{ResourceProvider.GetString("LOCSaveManagerMsgDeleteCloudDataFailed")}\n{ex.Message}",
                    "Error");
            }
        }

        /// <summary>
        /// 配置云服务商（包含 Rclone 安装检查）
        /// </summary>
        private async System.Threading.Tasks.Task ConfigureCloudProviderAsync()
        {
            try
            {
                var rcloneService = plugin.GetRcloneService();
                if (rcloneService == null)
                {
                    plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                        "Cloud sync service not initialized.",
                        "Error");
                    return;
                }

                // 检查 Rclone 是否已安装，如果没有则提示安装
                if (!rcloneService.IsRcloneInstalled)
                {
                    var installConfirm = plugin.PlayniteApi.Dialogs.ShowMessage(
                        ResourceProvider.GetString("LOCSaveManagerMsgInstallRclone"),
                        ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);

                    if (installConfirm != System.Windows.MessageBoxResult.Yes)
                    {
                        return;
                    }

                    bool installed = false;
                    
                    // 使用 ActivateGlobalProgress 安装 Rclone（带进度）
                    plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                    {
                        progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgRcloneInstalling");
                        progressArgs.IsIndeterminate = false;
                        progressArgs.CurrentProgressValue = 0;
                        progressArgs.ProgressMaxValue = 100;

                        var progress = new Progress<(string message, int percentage)>(report =>
                        {
                            progressArgs.Text = report.message;
                            if (report.percentage >= 0)
                            {
                                progressArgs.CurrentProgressValue = report.percentage;
                            }
                        });

                        var installTask = rcloneService.InstallRcloneAsync(progress, progressArgs.CancelToken);
                        installTask.Wait();
                        installed = installTask.Result;
                    }, new Playnite.SDK.GlobalProgressOptions(
                        ResourceProvider.GetString("LOCSaveManagerMsgRcloneInstalling"), true));

                    if (!installed)
                    {
                        plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                            ResourceProvider.GetString("LOCSaveManagerMsgRcloneInstallFailed"),
                            "Error");
                        return;
                    }

                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        ResourceProvider.GetString("LOCSaveManagerMsgRcloneInstalled"),
                        ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }

                var provider = (Models.CloudProvider)CloudProvider;
                var displayName = Models.CloudProviderHelper.GetDisplayName(provider);

                var confirmResult = plugin.PlayniteApi.Dialogs.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgConfigureCloud"), displayName),
                    ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Information);

                if (confirmResult != System.Windows.MessageBoxResult.OK)
                {
                    return;
                }

                bool configured = false;

                // 使用 ActivateGlobalProgress 配置云服务
                plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    progressArgs.Text = string.Format(ResourceProvider.GetString("LOCSaveManagerMsgConfiguringCloud"), displayName);
                    progressArgs.IsIndeterminate = true;

                    var configTask = rcloneService.ConfigureCloudProviderAsync(provider);
                    configTask.Wait(); // 同步等待
                    configured = configTask.Result;
                }, new Playnite.SDK.GlobalProgressOptions(
                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgConfiguringCloud"), displayName), false));

                if (configured)
                {
                    // 配置完成后先验证连接
                    bool connectionSuccess = false;
                    plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                    {
                        progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgVerifyingCloud");
                        progressArgs.IsIndeterminate = true;
                        
                        var testTask = rcloneService.TestConnectionAsync(provider);
                        testTask.Wait();
                        connectionSuccess = testTask.Result;
                    }, new Playnite.SDK.GlobalProgressOptions(
                        ResourceProvider.GetString("LOCSaveManagerMsgVerifyingCloud"), false));

                    if (!connectionSuccess)
                    {
                        plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                            ResourceProvider.GetString("LOCSaveManagerMsgConnectionFailed"),
                            ResourceProvider.GetString("LOCSaveManagerError"));
                        return;
                    }

                    // 连接验证成功后启用云同步
                    CloudSyncEnabled = true;
                    SaveSettings();

                    var cloudSyncManager = plugin.GetCloudSyncManager();
                    if (cloudSyncManager != null)
                    {
                        // 1. 检测云端是否已有 config.json
                        bool cloudHasData = false;
                        bool? cloudCheckResult = null;
                        plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                        {
                            progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgInitializingCloud");
                            progressArgs.IsIndeterminate = true;

                            try
                            {
                                cloudCheckResult = System.Threading.Tasks.Task.Run(async () => 
                                    await rcloneService.RemoteFileExistsAsync("config.json", provider)
                                ).GetAwaiter().GetResult();
                                cloudHasData = cloudCheckResult == true;
                            }
                            catch { }
                        }, new Playnite.SDK.GlobalProgressOptions(
                            ResourceProvider.GetString("LOCSaveManagerMsgInitializingCloud"), false));

                        // 检查连接是否失败
                        if (cloudCheckResult == null)
                        {
                            plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                                ResourceProvider.GetString("LOCSaveManagerMsgCloudConnectionFailed"),
                                ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"));
                            return;
                        }

                        if (cloudHasData)
                        {
                            // 云端已有数据，显示三个选项
                            var options = new List<Playnite.SDK.MessageBoxOption>
                            {
                                new Playnite.SDK.MessageBoxOption(
                                    ResourceProvider.GetString("LOCSaveManagerBtnPullAllData"), false, false),
                                new Playnite.SDK.MessageBoxOption(
                                    ResourceProvider.GetString("LOCSaveManagerBtnPullConfigOnly"), true, false),
                                new Playnite.SDK.MessageBoxOption(
                                    ResourceProvider.GetString("LOCSaveManagerBtnSkipPull"), false, true)
                            };

                            var selectedOption = plugin.PlayniteApi.Dialogs.ShowMessage(
                                ResourceProvider.GetString("LOCSaveManagerMsgCloudHasDataOptions"),
                                ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                                System.Windows.MessageBoxImage.Question,
                                options);

                            if (selectedOption == options[0])
                            {
                                // 记录当前的配置ID列表
                                var existingConfigIds = plugin.GetBackupService().GetAllGameConfigs()
                                    .Select(c => c.ConfigId)
                                    .ToHashSet();

                                // 拉取所有数据
                                bool success = false;
                                plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                                {
                                    progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgPullingData");
                                    progressArgs.IsIndeterminate = true;

                                    try
                                    {
                                        success = System.Threading.Tasks.Task.Run(async () =>
                                            await cloudSyncManager.PullAllDataFromCloudAsync(null, System.Threading.CancellationToken.None)
                                        ).GetAwaiter().GetResult();
                                    }
                                    catch { }
                                }, new Playnite.SDK.GlobalProgressOptions(
                                    ResourceProvider.GetString("LOCSaveManagerMsgPullingData"), false));

                                plugin.PlayniteApi.Dialogs.ShowMessage(
                                    success ? ResourceProvider.GetString("LOCSaveManagerMsgPullDataSuccessDetail")
                                            : ResourceProvider.GetString("LOCSaveManagerMsgPullDataFailed"),
                                    ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                                    System.Windows.MessageBoxButton.OK,
                                    success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);

                                if (success)
                                {
                                    // 检查新增配置并显示匹配对话框
                                    var newConfigs = plugin.GetBackupService().GetAllGameConfigs()
                                        .Where(c => !existingConfigIds.Contains(c.ConfigId))
                                        .Select(c => c.ConfigId)
                                        .ToList();

                                    if (newConfigs.Count > 0)
                                    {
                                        plugin.ShowGameMatchingDialogForNewConfigs(newConfigs);
                                    }
                                }
                            }
                            else if (selectedOption == options[1])
                            {
                                // 记录当前的配置ID列表
                                var existingConfigIds = plugin.GetBackupService().GetAllGameConfigs()
                                    .Select(c => c.ConfigId)
                                    .ToHashSet();

                                // 仅拉取配置文件
                                bool success = false;
                                plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                                {
                                    progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgPullingConfigOnly");
                                    progressArgs.IsIndeterminate = true;

                                    try
                                    {
                                        success = System.Threading.Tasks.Task.Run(async () =>
                                            await cloudSyncManager.PullConfigOnlyAsync(provider, System.Threading.CancellationToken.None)
                                        ).GetAwaiter().GetResult();
                                    }
                                    catch { }
                                }, new Playnite.SDK.GlobalProgressOptions(
                                    ResourceProvider.GetString("LOCSaveManagerMsgPullingConfigOnly"), false));

                                plugin.PlayniteApi.Dialogs.ShowMessage(
                                    success ? ResourceProvider.GetString("LOCSaveManagerMsgPullConfigSuccess")
                                            : ResourceProvider.GetString("LOCSaveManagerMsgPullDataFailed"),
                                    ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                                    System.Windows.MessageBoxButton.OK,
                                    success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);

                                if (success)
                                {
                                    // 检查新增配置并显示匹配对话框
                                    var newConfigs = plugin.GetBackupService().GetAllGameConfigs()
                                        .Where(c => !existingConfigIds.Contains(c.ConfigId))
                                        .Select(c => c.ConfigId)
                                        .ToList();

                                    if (newConfigs.Count > 0)
                                    {
                                        plugin.ShowGameMatchingDialogForNewConfigs(newConfigs);
                                    }
                                }
                            }
                            else
                            {
                                // 暂不拉取
                                plugin.PlayniteApi.Dialogs.ShowMessage(
                                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudConfigured"), displayName),
                                    ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Information);
                            }
                        }
                        else
                        {
                            // 云端没有数据，先上传 config.json，再询问是否上传备份
                            plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                            {
                                progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgInitializingCloud");
                                progressArgs.IsIndeterminate = true;

                                try
                                {
                                    System.Threading.Tasks.Task.Run(async () => 
                                        await cloudSyncManager.UploadConfigToCloudAsync()
                                    ).GetAwaiter().GetResult();
                                }
                                catch { }
                            }, new Playnite.SDK.GlobalProgressOptions(
                                ResourceProvider.GetString("LOCSaveManagerMsgInitializingCloud"), false));

                            // 询问是否上传现有备份
                            var uploadConfirm = plugin.PlayniteApi.Dialogs.ShowMessage(
                                ResourceProvider.GetString("LOCSaveManagerMsgConfirmFirstSync"),
                                ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                                System.Windows.MessageBoxButton.YesNo,
                                System.Windows.MessageBoxImage.Question);

                            if (uploadConfirm == System.Windows.MessageBoxResult.Yes)
                            {
                                // 直接调用推送所有数据的核心逻辑
                                DoPushAllData(cloudSyncManager);
                            }
                            else
                            {
                                plugin.PlayniteApi.Dialogs.ShowMessage(
                                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudConfigured"), displayName),
                                    ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Information);
                            }
                        }
                    }
                }
                else
                {
                    plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudConfigFailed"), displayName),
                        "Error");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to configure cloud provider");
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 从云端拉取所有数据
        /// </summary>
        private async System.Threading.Tasks.Task PullAllDataAsync()
        {
            try
            {
                var cloudSyncManager = plugin.GetCloudSyncManager();
                if (cloudSyncManager == null)
                {
                    plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                        "Cloud sync not enabled.",
                        "Error");
                    return;
                }

                var confirmResult = plugin.PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgConfirmPullAllData"),
                    ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (confirmResult != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }

                bool success = false;

                // 使用 ActivateGlobalProgress 在前台显示进度
                plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgPullingData");

                    var pullTask = cloudSyncManager.PullAllDataFromCloudAsync(
                        null, 
                        progressArgs.CancelToken,
                        (current, total) =>
                        {
                            progressArgs.CurrentProgressValue = current;
                            progressArgs.ProgressMaxValue = total;
                        });
                    pullTask.Wait();
                    success = pullTask.Result;
                }, new Playnite.SDK.GlobalProgressOptions(
                    ResourceProvider.GetString("LOCSaveManagerMsgPullingData"), false)
                {
                    IsIndeterminate = false
                });

                if (success)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        ResourceProvider.GetString("LOCSaveManagerMsgPullDataSuccessDetail"),
                        ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                        ResourceProvider.GetString("LOCSaveManagerMsgPullDataFailed"),
                        "Error");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to pull all data");
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 推送所有数据到云端
        /// </summary>
        private async System.Threading.Tasks.Task PushAllDataAsync()
        {
            try
            {
                var cloudSyncManager = plugin.GetCloudSyncManager();
                if (cloudSyncManager == null)
                {
                    plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                        "Cloud sync not enabled.",
                        "Error");
                    return;
                }

                var confirmResult = plugin.PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgConfirmPushAllData"),
                    ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (confirmResult != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }

                DoPushAllData(cloudSyncManager);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to push all data");
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 推送所有数据的核心逻辑（不含确认对话框）
        /// </summary>
        private void DoPushAllData(Services.CloudSyncManager cloudSyncManager)
        {
            int uploadedCount = 0;

            // 使用 ActivateGlobalProgress 在前台显示进度
            plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgPushingData");
                progressArgs.IsIndeterminate = false;

                var pushTask = cloudSyncManager.UploadAllBackupsToCloudAsync(
                    null, 
                    progressArgs.CancelToken,
                    (current, total) =>
                    {
                        progressArgs.CurrentProgressValue = current;
                        progressArgs.ProgressMaxValue = total;
                        progressArgs.Text = string.Format(
                            ResourceProvider.GetString("LOCSaveManagerMsgPushingProgress"), 
                            current, total);
                    });
                pushTask.Wait();
                uploadedCount = pushTask.Result;
            }, new Playnite.SDK.GlobalProgressOptions(
                ResourceProvider.GetString("LOCSaveManagerMsgPushingData"), true)
            {
                IsIndeterminate = false
            });

            plugin.PlayniteApi.Dialogs.ShowMessage(
                string.Format(ResourceProvider.GetString("LOCSaveManagerMsgPushDataSuccess"), uploadedCount),
                ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        /// <summary>
        /// 验证云服务连接
        /// </summary>
        private void VerifyCloud()
        {
            try
            {
                var rcloneService = plugin.GetRcloneService();
                if (rcloneService == null || !rcloneService.IsRcloneInstalled)
                {
                    var msg = ResourceProvider.GetString("LOCSaveManagerMsgRcloneNotInstalled");
                    plugin.PlayniteApi.Dialogs.ShowMessage(msg, ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"));
                    return;
                }

                var provider = (Models.CloudProvider)CloudProvider;
                var displayName = Models.CloudProviderHelper.GetDisplayName(provider);

                if (!rcloneService.IsConfigured(provider))
                {
                    var msg = string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudNotConfigured"), displayName);
                    plugin.PlayniteApi.Dialogs.ShowMessage(msg, ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"));
                    return;
                }

                logger.Info("Starting cloud verification...");
                IsVerifyingCloud = true;
                Services.RcloneService.CloudVerifyResult result = null;
                Exception verifyException = null;

                // 使用 ActivateGlobalProgress 显示等待弹窗
                plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    try
                    {
                        // 在线程池中执行，避免死锁
                        var task = System.Threading.Tasks.Task.Run(async () => 
                            await rcloneService.VerifyCloudServiceAsync(provider, progressArgs.CancelToken)
                        );
                        
                        // 等待任务完成
                        while (!task.IsCompleted && !progressArgs.CancelToken.IsCancellationRequested)
                        {
                            System.Threading.Thread.Sleep(100);
                        }
                        
                        if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                        {
                            result = task.Result;
                        }
                    }
                    catch (Exception ex)
                    {
                        verifyException = ex;
                    }
                }, new Playnite.SDK.GlobalProgressOptions(
                    ResourceProvider.GetString("LOCSaveManagerMsgVerifyingCloud"), true)
                {
                    IsIndeterminate = true
                });

                // 处理异常
                if (verifyException != null)
                {
                    logger.Error(verifyException, "Failed to verify cloud service");
                    var errorMsg = $"❌ {verifyException.Message}";
                    CloudVerifyStatus = errorMsg;
                    IsVerifyingCloud = false;
                    plugin.PlayniteApi.Dialogs.ShowErrorMessage(errorMsg, "Error");
                    return;
                }

                if (result == null)
                {
                    IsVerifyingCloud = false;
                    return;
                }

                // 构建状态信息
                var statusBuilder = new System.Text.StringBuilder();

                if (result.ConnectionSuccessful)
                {
                    statusBuilder.AppendLine($"✅ {displayName} {ResourceProvider.GetString("LOCSaveManagerMsgConnectionSuccess")}");
                }
                else
                {
                    statusBuilder.AppendLine($"❌ {displayName} {ResourceProvider.GetString("LOCSaveManagerMsgConnectionFailed")}");
                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        statusBuilder.AppendLine($"   {result.ErrorMessage}");
                    }
                }

                if (result.BackupExists)
                {
                    // 转换为本地时间（北京时间）
                    var localTime = result.BackupModTime?.ToLocalTime();
                    var timeStr = localTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown";
                    statusBuilder.AppendLine($"✅ {string.Format(ResourceProvider.GetString("LOCSaveManagerMsgBackupExists"), timeStr)}");
                }
                else
                {
                    statusBuilder.AppendLine($"❌ {ResourceProvider.GetString("LOCSaveManagerMsgNoBackupOnCloud")}");
                }

                var finalStatus = statusBuilder.ToString().Trim();
                IsVerifyingCloud = false;
                logger.Info($"Verification complete: {finalStatus}");

                // 显示结果对话框
                plugin.PlayniteApi.Dialogs.ShowMessage(
                    finalStatus,
                    ResourceProvider.GetString("LOCSaveManagerTitleCloudSync"),
                    System.Windows.MessageBoxButton.OK,
                    result.ConnectionSuccessful ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to verify cloud service");
                var errorMsg = $"❌ {ex.Message}";
                IsVerifyingCloud = false;
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(errorMsg, "Error");
            }
        }

        /// <summary>
        /// 立即同步配置
        /// </summary>
        private async System.Threading.Tasks.Task SyncConfigNowAsync()
        {
            if (!CloudSyncEnabled)
            {
                plugin.PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncDisabled"),
                    "Save Manager");
                return;
            }

            try
            {
                var cloudSyncManager = plugin.GetCloudSyncManager();
                if (cloudSyncManager == null)
                {
                    plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                        "Cloud sync manager not initialized.", "Error");
                    return;
                }

                bool success = false;
                Exception syncException = null;

                plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    progressArgs.IsIndeterminate = true;
                    progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgSyncingConfig");

                    try
                    {
                        var task = cloudSyncManager.SyncConfigFromCloudAsync();
                        task.Wait();
                        var result = task.Result;
                        success = result.Success;
                        if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
                        {
                            syncException = new Exception(result.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        syncException = ex;
                        success = false;
                    }
                }, new GlobalProgressOptions(
                    ResourceProvider.GetString("LOCSaveManagerMsgSyncingConfig"), false)
                {
                    IsIndeterminate = true
                });

                if (success)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        ResourceProvider.GetString("LOCSaveManagerMsgSyncConfigSuccess"),
                        "Save Manager",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else if (syncException != null)
                {
                    plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                        syncException.Message, "Sync Error");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to sync config");
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        /// <summary>
        /// 验证云端所有备份文件的可用性
        /// </summary>
        private async System.Threading.Tasks.Task VerifyCloudBackupsAsync()
        {
            if (plugin == null) return;

            if (!CloudSyncEnabled)
            {
                plugin.PlayniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncDisabled"),
                    "Save Manager");
                return;
            }

            var backupService = plugin.GetBackupService();
            var cloudSyncManager = plugin.GetCloudSyncManager();
            
            if (backupService == null || cloudSyncManager == null)
            {
                plugin.PlayniteApi.Dialogs.ShowErrorMessage("Services not available", "Error");
                return;
            }

            try
            {
                // 收集所有需要验证的备份
                var allConfigs = backupService.GetAllGameConfigs();
                var backupsToVerify = new List<(string gameName, SaveManager.Models.SaveBackup backup, string cloudPath)>();

                foreach (var config in allConfigs)
                {
                    var backups = backupService.GetBackupsByConfigId(config.ConfigId);
                    
                    // 使用配置中的游戏名称
                    var gameName = config.GameName ?? config.ConfigId.ToString();

                    foreach (var backup in backups)
                    {
                        var cloudPath = cloudSyncManager.GetBackupCloudPath(backup, gameName);
                        backupsToVerify.Add((gameName, backup, cloudPath));
                    }
                }

                if (backupsToVerify.Count == 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        ResourceProvider.GetString("LOCSaveManagerMsgNoCloudBackups"),
                        "Save Manager");
                    return;
                }

                // 验证每个备份
                var invalidBackups = new List<(string gameName, string backupName, string note, string reason)>();
                int current = 0;
                int total = backupsToVerify.Count;

                plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    progressArgs.IsIndeterminate = false;
                    progressArgs.ProgressMaxValue = total;

                    foreach (var (gameName, backup, cloudPath) in backupsToVerify)
                    {
                        if (progressArgs.CancelToken.IsCancellationRequested)
                            break;

                        current++;
                        progressArgs.CurrentProgressValue = current;
                        progressArgs.Text = string.Format(
                            ResourceProvider.GetString("LOCSaveManagerMsgVerifyingBackups"),
                            current, total);

                        try
                        {
                            var task = cloudSyncManager.GetCloudFileInfoAsync(cloudPath);
                            task.Wait();
                            var (exists, size) = task.Result;

                            if (!exists)
                            {
                                invalidBackups.Add((gameName, backup.Name, backup.Description ?? "", "Not found"));
                            }
                            else if (size == 0)
                            {
                                invalidBackups.Add((gameName, backup.Name, backup.Description ?? "", "0 KB"));
                            }
                        }
                        catch (Exception ex)
                        {
                            invalidBackups.Add((gameName, backup.Name, backup.Description ?? "", ex.Message));
                        }
                    }
                }, new GlobalProgressOptions(
                    ResourceProvider.GetString("LOCSaveManagerMsgVerifyingBackups"), true)
                {
                    IsIndeterminate = false
                });

                // 显示结果
                if (invalidBackups.Count == 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgAllBackupsValid"), total),
                        "Save Manager",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    // 构建无效备份列表
                    var sb = new System.Text.StringBuilder();
                    foreach (var (gameName, backupName, note, reason) in invalidBackups)
                    {
                        sb.AppendLine($"• {gameName} - {backupName}");
                        if (!string.IsNullOrEmpty(note))
                            sb.AppendLine($"  Note: {note}");
                        sb.AppendLine($"  Reason: {reason}");
                        sb.AppendLine();
                    }

                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgInvalidBackupsFound"),
                            invalidBackups.Count, sb.ToString().TrimEnd()),
                        "Save Manager",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to verify cloud backups");
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        public void BeginEdit()
        {
            // 保存当前值用于取消时恢复
            editingCustomBackupPath = CustomBackupPath;
            editingAutoBackupOnGameExit = AutoBackupOnGameExit;
            editingConfirmBeforeBackup = ConfirmBeforeBackup;
            editingMaxBackupCount = MaxAutoBackupCount;
            editingRealtimeSyncEnabled = RealtimeSyncEnabled;
            editingShowSidebarButton = ShowSidebarButton;
        }

        public void CancelEdit()
        {
            // 恢复原始值
            CustomBackupPath = editingCustomBackupPath;
            AutoBackupOnGameExit = editingAutoBackupOnGameExit;
            ConfirmBeforeBackup = editingConfirmBeforeBackup;
            MaxAutoBackupCount = editingMaxBackupCount;
            RealtimeSyncEnabled = editingRealtimeSyncEnabled;
            ShowSidebarButton = editingShowSidebarButton;
        }

        public void EndEdit()
        {
            // 保存设置到自定义文件（不使用 Playnite SDK 的 SavePluginSettings）
            SaveSettings();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            // 验证自定义备份路径
            if (!string.IsNullOrEmpty(CustomBackupPath) && !System.IO.Directory.Exists(CustomBackupPath))
            {
                errors.Add("自定义备份目录不存在，请选择有效的目录。");
            }

            // 验证自动备份最大数量
            if (MaxAutoBackupCount < 0)
            {
                errors.Add("自动备份最大数量不能为负数。");
            }

            return errors.Count == 0;
        }
    }
}
