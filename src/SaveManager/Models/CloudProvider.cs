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
        /// 通用 WebDAV
        /// </summary>
        WebDAV = 2,

        /// <summary>
        /// Cloudflare R2 (S3 兼容)
        /// </summary>
        CloudflareR2 = 3,

        /// <summary>
        /// pCloud
        /// </summary>
        pCloud = 4
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
                case CloudProvider.WebDAV:
                    return "webdav";
                case CloudProvider.CloudflareR2:
                    return "cloudflare-r2";
                case CloudProvider.pCloud:
                    return "pcloud";
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
                case CloudProvider.WebDAV:
                    return "webdav";
                case CloudProvider.CloudflareR2:
                    return "s3";
                case CloudProvider.pCloud:
                    return "pcloud";
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
                case CloudProvider.WebDAV:
                    return "WebDAV";
                case CloudProvider.CloudflareR2:
                    return "Cloudflare R2";
                case CloudProvider.pCloud:
                    return "pCloud";
                default:
                    return provider.ToString();
            }
        }

        /// <summary>
        /// 是否需要 WebDAV 配置（URL、用户名、密码）
        /// </summary>
        public static bool RequiresWebDAVConfig(CloudProvider provider)
        {
            return provider == CloudProvider.WebDAV;
        }

        /// <summary>
        /// 是否需要 S3 配置（Access Key, Secret Key, Endpoint）
        /// </summary>
        public static bool RequiresS3Config(CloudProvider provider)
        {
            return provider == CloudProvider.CloudflareR2;
        }

        /// <summary>
        /// 是否需要 OAuth 配置（浏览器授权）
        /// </summary>
        public static bool RequiresOAuthConfig(CloudProvider provider)
        {
            return provider == CloudProvider.GoogleDrive || 
                   provider == CloudProvider.OneDrive || 
                   provider == CloudProvider.pCloud;
        }
    }
}
