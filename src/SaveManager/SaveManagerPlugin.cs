using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
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
            backupService = new BackupService(dataPath, logger, PlayniteApi);

            // 设置属性以启用设置视图
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            logger.Info("Save Manager plugin initialized");
        }

        /// <summary>
        /// 游戏右键菜单项
        /// </summary>
        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = "存档管理",
                MenuSection = "存档管理",
                Icon = "💾",
                Action = (menuArgs) =>
                {
                    if (menuArgs.Games.Count == 1)
                    {
                        OpenSaveManager(menuArgs.Games[0]);
                    }
                    else
                    {
                        PlayniteApi.Dialogs.ShowMessage("请只选择一个游戏进行存档管理。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            };

            yield return new GameMenuItem
            {
                Description = "快速备份",
                MenuSection = "存档管理",
                Icon = "📦",
                Action = (menuArgs) =>
                {
                    foreach (var game in menuArgs.Games)
                    {
                        QuickBackup(game);
                    }
                }
            };

            yield return new GameMenuItem
            {
                Description = "快速还原（最近备份）",
                MenuSection = "存档管理",
                Icon = "↩️",
                Action = (menuArgs) =>
                {
                    if (menuArgs.Games.Count == 1)
                    {
                        QuickRestore(menuArgs.Games[0]);
                    }
                    else
                    {
                        PlayniteApi.Dialogs.ShowMessage("请只选择一个游戏进行还原。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            };
        }

        /// <summary>
        /// 主菜单项
        /// </summary>
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "打开备份文件夹",
                MenuSection = "@存档管理",
                Action = (menuArgs) =>
                {
                    var backupsPath = System.IO.Path.Combine(GetPluginUserDataPath(), "Backups");
                    System.IO.Directory.CreateDirectory(backupsPath);
                    System.Diagnostics.Process.Start("explorer.exe", backupsPath);
                }
            };

            yield return new MainMenuItem
            {
                Description = "关于 Save Manager",
                MenuSection = "@存档管理",
                Action = (menuArgs) =>
                {
                    PlayniteApi.Dialogs.ShowMessage(
                        "Save Manager v1.0.0\n\n" +
                        "一个用于管理游戏存档备份的Playnite插件。\n\n" +
                        "功能：\n" +
                        "• 为每个游戏配置存档路径（支持文件夹和文件）\n" +
                        "• 创建存档备份（ZIP压缩格式）\n" +
                        "• 为备份添加备注说明\n" +
                        "• 一键还原到任意备份\n\n" +
                        "使用方法：右键游戏 → Save Manager → 存档管理",
                        "关于 Save Manager",
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
                window.Title = $"存档管理 - {game.Name}";
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();

                var viewModel = new SaveManagerViewModel(game, PlayniteApi, backupService);
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
                PlayniteApi.Dialogs.ShowErrorMessage($"打开存档管理器失败: {ex.Message}", "错误");
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
                        $"游戏 \"{game.Name}\" 尚未配置存档路径。\n\n是否现在配置？",
                        "未配置存档路径",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        OpenSaveManager(game);
                    }
                    return;
                }

                // 检查是否需要确认
                if (settings.ConfirmBeforeBackup)
                {
                    var confirmResult = PlayniteApi.Dialogs.ShowMessage(
                        $"确定要为游戏 \"{game.Name}\" 创建存档备份吗？",
                        "确认备份",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirmResult != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                // 获取备份备注
                var noteResult = PlayniteApi.Dialogs.SelectString(
                    "请输入备份备注（可选）：",
                    "备份备注",
                    "快速备份");

                if (!noteResult.Result)
                {
                    return;
                }

                var backup = backupService.CreateBackup(game.Id, game.Name, noteResult.SelectedString);
                PlayniteApi.Dialogs.ShowMessage(
                    $"备份创建成功！\n\n文件名: {backup.Name}\n大小: {backup.FormattedSize}",
                    "成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Quick backup failed for game {game.Name}");
                PlayniteApi.Dialogs.ShowErrorMessage($"快速备份失败: {ex.Message}", "错误");
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
                        $"游戏 \"{game.Name}\" 没有可用的备份。",
                        "无备份",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var latestBackup = backups[0];
                var result = PlayniteApi.Dialogs.ShowMessage(
                    $"确定要还原到最近的备份吗？\n\n" +
                    $"备份名称: {latestBackup.Name}\n" +
                    $"创建时间: {latestBackup.FormattedDate}\n" +
                    $"备注: {(string.IsNullOrEmpty(latestBackup.Description) ? "无" : latestBackup.Description)}\n\n" +
                    "⚠️ 这将覆盖当前的存档文件！",
                    "确认还原",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    backupService.RestoreBackup(latestBackup);
                    PlayniteApi.Dialogs.ShowMessage("备份还原成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Quick restore failed for game {game.Name}");
                PlayniteApi.Dialogs.ShowErrorMessage($"快速还原失败: {ex.Message}", "错误");
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
    }
}
