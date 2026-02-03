using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        // 实时同步设置（默认启用）
        private bool realtimeSyncEnabled = true;

        /// <summary>
        /// 游戏前后实时同步游玩存档
        /// </summary>
        public bool RealtimeSyncEnabled
        {
            get => realtimeSyncEnabled;
            set
            {
                // 如果用户尝试从启用改为禁用，弹出警告
                if (realtimeSyncEnabled && !value && plugin?.PlayniteApi != null)
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
                        RealtimeSyncEnabled = savedSettings.RealtimeSyncEnabled;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to load plugin settings from settings.json");
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
                        ResourceProvider.GetString("LOCSaveManagerMsgDeleteAllDataSuccess"),
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

        public void BeginEdit()
        {
            // 保存当前值用于取消时恢复
            editingCustomBackupPath = CustomBackupPath;
            editingAutoBackupOnGameExit = AutoBackupOnGameExit;
            editingConfirmBeforeBackup = ConfirmBeforeBackup;
            editingMaxBackupCount = MaxAutoBackupCount;
            editingRealtimeSyncEnabled = RealtimeSyncEnabled;
        }

        public void CancelEdit()
        {
            // 恢复原始值
            CustomBackupPath = editingCustomBackupPath;
            AutoBackupOnGameExit = editingAutoBackupOnGameExit;
            ConfirmBeforeBackup = editingConfirmBeforeBackup;
            MaxAutoBackupCount = editingMaxBackupCount;
            RealtimeSyncEnabled = editingRealtimeSyncEnabled;
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
