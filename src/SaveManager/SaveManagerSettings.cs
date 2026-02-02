using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Data;
using SaveManager.ViewModels;

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

        // 用于存储备份的原始值
        [DontSerialize]
        private string editingCustomBackupPath;
        [DontSerialize]
        private bool editingAutoBackupOnGameExit;
        [DontSerialize]
        private bool editingConfirmBeforeBackup;
        [DontSerialize]
        private int editingMaxBackupCount;

        // 命令
        [DontSerialize]
        public ICommand BrowseBackupPathCommand { get; }

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

        public void BeginEdit()
        {
            // 保存当前值用于取消时恢复
            editingCustomBackupPath = CustomBackupPath;
            editingAutoBackupOnGameExit = AutoBackupOnGameExit;
            editingConfirmBeforeBackup = ConfirmBeforeBackup;
            editingMaxBackupCount = MaxAutoBackupCount;
        }

        public void CancelEdit()
        {
            // 恢复原始值
            CustomBackupPath = editingCustomBackupPath;
            AutoBackupOnGameExit = editingAutoBackupOnGameExit;
            ConfirmBeforeBackup = editingConfirmBeforeBackup;
            MaxAutoBackupCount = editingMaxBackupCount;
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
