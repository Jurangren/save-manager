using System;

namespace SaveManager.Models
{
    /// <summary>
    /// 支持的云服务商
    /// </summary>
    public enum CloudProvider
    {
        /// <summary>
        /// Google Drive
        /// </summary>
        GoogleDrive = 0,

        /// <summary>
        /// Microsoft OneDrive
        /// </summary>
        OneDrive = 1,

        /// <summary>
        /// 坚果云 (WebDAV)
        /// </summary>
        Jianguoyun = 2
    }

    /// <summary>
    /// 云服务商帮助类
    /// </summary>
    public static class CloudProviderHelper
    {
        /// <summary>
        /// 获取 Rclone 配置名称
        /// </summary>
        public static string GetConfigName(CloudProvider provider)
        {
            switch (provider)
            {
                case CloudProvider.GoogleDrive:
                    return "gdrive";
                case CloudProvider.OneDrive:
                    return "onedrive";
                case CloudProvider.Jianguoyun:
                    return "jianguoyun";
                default:
                    throw new ArgumentException($"Unsupported provider: {provider}");
            }
        }

        /// <summary>
        /// 获取 Rclone 类型
        /// </summary>
        public static string GetProviderType(CloudProvider provider)
        {
            switch (provider)
            {
                case CloudProvider.GoogleDrive:
                    return "drive";
                case CloudProvider.OneDrive:
                    return "onedrive";
                case CloudProvider.Jianguoyun:
                    return "webdav";
                default:
                    throw new ArgumentException($"Unsupported provider: {provider}");
            }
        }

        /// <summary>
        /// 获取显示名称
        /// </summary>
        public static string GetDisplayName(CloudProvider provider)
        {
            switch (provider)
            {
                case CloudProvider.GoogleDrive:
                    return "Google Drive";
                case CloudProvider.OneDrive:
                    return "OneDrive";
                case CloudProvider.Jianguoyun:
                    return "坚果云";
                default:
                    return provider.ToString();
            }
        }

        /// <summary>
        /// 是否需要 WebDAV 配置（用户名密码）
        /// </summary>
        public static bool RequiresWebDAVConfig(CloudProvider provider)
        {
            return provider == CloudProvider.Jianguoyun;
        }
    }
}
