using System;
using Microsoft.Toolkit.Uwp.Notifications;
using Playnite.SDK;

namespace SaveManager.Services
{
    /// <summary>
    /// Windows Toast 通知服务
    /// </summary>
    public static class ToastNotificationService
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string APP_ID = "SaveManager";

        /// <summary>
        /// 显示成功通知
        /// </summary>
        /// <param name="title">通知标题</param>
        /// <param name="message">通知内容</param>
        public static void ShowSuccess(string title, string message)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .SetToastDuration(ToastDuration.Short)
                    .Show();
            }
            catch (Exception ex)
            {
                logger.Warn($"Failed to show Windows toast notification: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示错误通知
        /// </summary>
        /// <param name="title">通知标题</param>
        /// <param name="message">通知内容</param>
        public static void ShowError(string title, string message)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .SetToastDuration(ToastDuration.Long)
                    .Show();
            }
            catch (Exception ex)
            {
                logger.Warn($"Failed to show Windows toast notification: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示带有游戏图标的备份成功通知
        /// </summary>
        /// <param name="gameName">游戏名称</param>
        /// <param name="backupName">备份名称</param>
        /// <param name="iconPath">游戏图标路径（可选）</param>
        public static void ShowBackupSuccess(string gameName, string backupName, string iconPath = null)
        {
            try
            {
                var builder = new ToastContentBuilder()
                    .AddText(ResourceProvider.GetString("LOCSaveManagerToastBackupTitle"))
                    .AddText(string.Format(ResourceProvider.GetString("LOCSaveManagerToastBackupMessage"), gameName, backupName));

                // 如果有图标路径，添加图标
                if (!string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath))
                {
                    builder.AddAppLogoOverride(new Uri(iconPath));
                }

                builder.SetToastDuration(ToastDuration.Short)
                       .Show();
            }
            catch (Exception ex)
            {
                logger.Warn($"Failed to show Windows toast notification: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示备份失败通知
        /// </summary>
        /// <param name="gameName">游戏名称</param>
        /// <param name="errorMessage">错误信息</param>
        public static void ShowBackupError(string gameName, string errorMessage)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(ResourceProvider.GetString("LOCSaveManagerToastBackupErrorTitle"))
                    .AddText(string.Format(ResourceProvider.GetString("LOCSaveManagerToastBackupErrorMessage"), gameName, errorMessage))
                    .SetToastDuration(ToastDuration.Long)
                    .Show();
            }
            catch (Exception ex)
            {
                logger.Warn($"Failed to show Windows toast notification: {ex.Message}");
            }
        }
    }
}
