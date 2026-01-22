using System;
using System.Collections.Generic;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Data;
using SaveManager.ViewModels;

namespace SaveManager
{
    /// <summary>
    /// 插件设置类
    /// </summary>
    public class SaveManagerSettings : ObservableObject, ISettings
    {
        private readonly SaveManagerPlugin plugin;

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
        /// 最大备份数量
        /// </summary>
        public int MaxBackupCount
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

            // 加载保存的设置
            var savedSettings = plugin.LoadPluginSettings<SaveManagerSettings>();
            if (savedSettings != null)
            {
                CustomBackupPath = savedSettings.CustomBackupPath;
                AutoBackupOnGameExit = savedSettings.AutoBackupOnGameExit;
                ConfirmBeforeBackup = savedSettings.ConfirmBeforeBackup;
                MaxBackupCount = savedSettings.MaxBackupCount;
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
            editingMaxBackupCount = MaxBackupCount;
        }

        public void CancelEdit()
        {
            // 恢复原始值
            CustomBackupPath = editingCustomBackupPath;
            AutoBackupOnGameExit = editingAutoBackupOnGameExit;
            ConfirmBeforeBackup = editingConfirmBeforeBackup;
            MaxBackupCount = editingMaxBackupCount;
        }

        public void EndEdit()
        {
            // 保存设置
            plugin?.SavePluginSettings(this);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            // 验证自定义备份路径
            if (!string.IsNullOrEmpty(CustomBackupPath) && !System.IO.Directory.Exists(CustomBackupPath))
            {
                errors.Add("自定义备份目录不存在，请选择有效的目录。");
            }

            // 验证最大备份数量
            if (MaxBackupCount < 0)
            {
                errors.Add("最大备份数量不能为负数。");
            }

            return errors.Count == 0;
        }
    }
}
