using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using SaveManager.Models;

namespace SaveManager.Services
{
    /// <summary>
    /// Rclone 云同步服务
    /// </summary>
    public class RcloneService
    {
        private readonly ILogger logger;
        private readonly IPlayniteAPI playniteApi;
        private readonly string dataPath;

        // 路径定义
        private string ToolsPath => Path.Combine(dataPath, "Tools");
        private string RcloneExePath => Path.Combine(ToolsPath, "rclone.exe");
        private string RcloneConfigPath => Path.Combine(ToolsPath, "rclone.conf");

        // 远程根目录
        private const string RemoteRootFolder = "PlayniteSaveManager";

        /// <summary>
        /// 获取完整的远程根路径（对于 R2 需要包含 bucket 名称）
        /// </summary>
        private string GetFullRemoteRootPath(CloudProvider provider)
        {
            if (provider == CloudProvider.CloudflareR2)
            {
                var bucket = GetR2BucketName();
                return $"{bucket}/{RemoteRootFolder}";
            }
            return RemoteRootFolder;
        }

        // 重试设置
        private const int MaxRetries = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Rclone 是否已安装
        /// </summary>
        public bool IsRcloneInstalled => File.Exists(RcloneExePath);

        /// <summary>
        /// 配置是否有效
        /// </summary>
        public bool IsConfigured(CloudProvider provider)
        {
            if (!File.Exists(RcloneConfigPath)) return false;

            try
            {
                var content = File.ReadAllText(RcloneConfigPath);
                var configName = CloudProviderHelper.GetConfigName(provider);
                var providerType = CloudProviderHelper.GetProviderType(provider);

                // 检查配置节和类型
                return content.Contains($"[{configName}]") && content.Contains($"type = {providerType}");
            }
            catch
            {
                return false;
            }
        }

        public RcloneService(string dataPath, ILogger logger, IPlayniteAPI playniteApi)
        {
            this.dataPath = dataPath;
            this.logger = logger;
            this.playniteApi = playniteApi;

            // 确保工具目录存在
            Directory.CreateDirectory(ToolsPath);
        }

        #region Rclone 安装

        /// <summary>
        /// 下载并安装 Rclone
        /// </summary>
        public async Task<bool> InstallRcloneAsync(IProgress<(string message, int percentage)> progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                progress?.Report(("Getting latest Rclone version...", 0));
                logger.Info("Starting Rclone installation...");

                // 1. 获取下载链接
                var downloadUrl = await GetLatestRcloneZipUrlAsync();
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new Exception("Could not determine Rclone download URL");
                }

                // logger.Info($"Download URL: {downloadUrl}");
                progress?.Report(("Downloading Rclone...", 5));

                // 2. 下载 ZIP 文件（带进度）
                var zipPath = Path.Combine(ToolsPath, $"rclone_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(10);
                    
                    using (var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        
                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes > 0;
                        
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            var totalBytesRead = 0L;
                            int bytesRead;
                            
                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                                totalBytesRead += bytesRead;
                                
                                if (canReportProgress)
                                {
                                    var percentage = (int)((totalBytesRead * 80 / totalBytes) + 5); // 5-85%
                                    var downloadedMB = totalBytesRead / 1024.0 / 1024.0;
                                    var totalMB = totalBytes / 1024.0 / 1024.0;
                                    progress?.Report(($"Downloading Rclone... {downloadedMB:F1}/{totalMB:F1} MB", percentage));
                                }
                            }
                        }
                    }
                }

                if (cancellationToken.IsCancellationRequested) return false;

                progress?.Report(("Extracting Rclone...", 90));

                // 3. 解压
                var tempExtractPath = Path.Combine(ToolsPath, $"temp_extract_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.CreateDirectory(tempExtractPath);

                ZipFile.ExtractToDirectory(zipPath, tempExtractPath);

                // 4. 找到 rclone.exe 并复制
                var rcloneFolder = Directory.GetDirectories(tempExtractPath)
                    .FirstOrDefault(d => d.Contains("windows"));

                if (rcloneFolder == null)
                {
                    throw new Exception("Could not find rclone folder in extracted files.");
                }

                var rcloneExeSource = Path.Combine(rcloneFolder, "rclone.exe");
                File.Copy(rcloneExeSource, RcloneExePath, true);

                progress?.Report(("Cleaning up...", 95));

                // 5. 清理
                File.Delete(zipPath);
                Directory.Delete(tempExtractPath, true);

                progress?.Report(("Rclone installed successfully!", 100));
                logger.Info("Rclone installed successfully");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to install Rclone");
                progress?.Report(($"Installation failed: {ex.Message}", -1));

                // 提示用户手动安装
                playniteApi.Dialogs.ShowErrorMessage(
                    $"Rclone 安装失败：{ex.Message}\n\n请尝试手动下载 Rclone 并解压到以下目录：\n{ToolsPath}\n确保 rclone.exe 位于该目录下。",
                    "安装失败"
                );

                // 尝试打开 Tools 目录以便用户手动安装
                try { Process.Start(ToolsPath); } catch { }

                return false;
            }
        }

        /// <summary>
        /// 获取最新 Rclone 下载链接
        /// </summary>
        private Task<string> GetLatestRcloneZipUrlAsync()
        {
            // 直接使用固定版本的下载链接，避免 GitHub API 问题或 JSON 解析依赖
            const string FixedUrl = "https://downloads.rclone.org/v1.68.2/rclone-v1.68.2-windows-amd64.zip";
            return Task.FromResult(FixedUrl);
        }

        #endregion

        #region 云服务认证

        /// <summary>
        /// 配置云服务（OAuth 认证或 WebDAV）
        /// </summary>
        public async Task<bool> ConfigureCloudProviderAsync(CloudProvider provider)
        {
            try
            {
                var configName = CloudProviderHelper.GetConfigName(provider);
                var providerType = CloudProviderHelper.GetProviderType(provider);
                var displayName = CloudProviderHelper.GetDisplayName(provider);

                logger.Info($"Configuring {displayName}...");

                // 坚果云使用 WebDAV，需要特殊处理
                if (CloudProviderHelper.RequiresWebDAVConfig(provider))
                {
                    return await ConfigureWebDAVProviderAsync(provider);
                }

                // Cloudflare R2 使用 S3 协议
                if (CloudProviderHelper.RequiresS3Config(provider))
                {
                    return await ConfigureS3ProviderAsync(provider);
                }

                // 执行 rclone config create（会打开浏览器进行 OAuth）
                var result = await ExecuteRcloneCommandAsync(
                    $"config create {configName} {providerType} --config \"{RcloneConfigPath}\"",
                    TimeSpan.FromMinutes(5),
                    hideWindow: false  // 需要用户交互
                );

                if (result.Success && IsConfigured(provider))
                {
                    logger.Info($"{displayName} configured successfully");
                    
                    // 创建 SaveManager 根目录
                    await EnsureRemoteRootDirectoryAsync(provider);
                    
                    return true;
                }
                else
                {
                    logger.Error($"Failed to configure {displayName}: {result.Error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to configure cloud provider: {provider}");
                return false;
            }
        }

        /// <summary>
        /// 确保远程根目录存在
        /// </summary>
        public async Task EnsureRemoteRootDirectoryAsync(CloudProvider provider)
        {
            try
            {
                var configName = CloudProviderHelper.GetConfigName(provider);
                var remotePath = $"{configName}:{GetFullRemoteRootPath(provider)}";
                
                logger.Info($"Creating remote root directory: {remotePath}");
                
                var result = await ExecuteRcloneCommandAsync(
                    $"mkdir \"{remotePath}\" --config \"{RcloneConfigPath}\"",
                    TimeSpan.FromSeconds(30)
                );
                
                if (result.Success)
                {
                    logger.Info("Remote root directory created/verified");
                }
                else
                {
                    logger.Warn($"Failed to create remote root directory: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to ensure remote root directory");
            }
        }

        /// <summary>
        /// 确保远程目录存在（包括父目录）
        /// </summary>
        public async Task EnsureRemoteDirectoryAsync(string remotePath, CloudProvider provider)
        {
            try
            {
                var configName = CloudProviderHelper.GetConfigName(provider);
                var fullRemotePath = $"{configName}:{GetFullRemoteRootPath(provider)}/{remotePath}";
                
                // rclone mkdir 会自动创建所有父目录
                var result = await ExecuteRcloneCommandAsync(
                    $"mkdir \"{fullRemotePath}\" --config \"{RcloneConfigPath}\"",
                    TimeSpan.FromSeconds(30)
                );
                
                if (!result.Success)
                {
                    logger.Warn($"Failed to create remote directory {remotePath}: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to ensure remote directory: {remotePath}");
            }
        }

        /// <summary>
        /// 配置通用 WebDAV 云服务
        /// </summary>
        private async Task<bool> ConfigureWebDAVProviderAsync(CloudProvider provider)
        {
            try
            {
                var configName = CloudProviderHelper.GetConfigName(provider);
                var displayName = CloudProviderHelper.GetDisplayName(provider);

                // 弹出输入框获取 WebDAV URL
                var urlInput = playniteApi.Dialogs.SelectString(
                    ResourceProvider.GetString("LOCSaveManagerMsgEnterWebDAVUrl"),
                    $"{displayName} - URL",
                    "https://"
                );

                if (!urlInput.Result || string.IsNullOrWhiteSpace(urlInput.SelectedString))
                {
                    logger.Info("User cancelled WebDAV URL input");
                    return false;
                }

                var webdavUrl = urlInput.SelectedString.Trim();
                // 确保 URL 以 / 结尾
                if (!webdavUrl.EndsWith("/"))
                {
                    webdavUrl += "/";
                }

                // 弹出输入框获取用户名
                var username = playniteApi.Dialogs.SelectString(
                    ResourceProvider.GetString("LOCSaveManagerMsgEnterWebDAVUsername"),
                    $"{displayName} - " + ResourceProvider.GetString("LOCSaveManagerMsgWebDAVUsername"),
                    ""
                );

                if (!username.Result || string.IsNullOrWhiteSpace(username.SelectedString))
                {
                    logger.Info("User cancelled WebDAV username input");
                    return false;
                }

                // 弹出输入框获取密码
                var password = playniteApi.Dialogs.SelectString(
                    ResourceProvider.GetString("LOCSaveManagerMsgEnterWebDAVPassword"),
                    $"{displayName} - " + ResourceProvider.GetString("LOCSaveManagerMsgWebDAVPassword"),
                    ""
                );

                if (!password.Result || string.IsNullOrWhiteSpace(password.SelectedString))
                {
                    logger.Info("User cancelled WebDAV password input");
                    return false;
                }

                // 先使用 rclone obscure 加密密码
                var obscureResult = await ExecuteRcloneCommandAsync(
                    $"obscure \"{password.SelectedString}\"",
                    TimeSpan.FromSeconds(10)
                );

                if (!obscureResult.Success || string.IsNullOrWhiteSpace(obscureResult.Output))
                {
                    logger.Error($"Failed to obscure password: {obscureResult.Error}");
                    playniteApi.Dialogs.ShowErrorMessage(
                        ResourceProvider.GetString("LOCSaveManagerMsgPasswordEncryptFailed"),
                        ResourceProvider.GetString("LOCSaveManagerMsgConfigFailed")
                    );
                    return false;
                }

                var obscuredPassword = obscureResult.Output.Trim();

                // 使用 rclone config create 创建 WebDAV 配置
                var result = await ExecuteRcloneCommandAsync(
                    $"config create {configName} webdav " +
                    $"url \"{webdavUrl}\" " +
                    $"vendor other " +
                    $"user \"{username.SelectedString}\" " +
                    $"pass \"{obscuredPassword}\" " +
                    $"--config \"{RcloneConfigPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                if (result.Success && IsConfigured(provider))
                {
                    logger.Info($"{displayName} configured successfully");
                    
                    // 创建 SaveManager 根目录
                    await EnsureRemoteRootDirectoryAsync(provider);
                    
                    playniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgWebDAVConfigSuccess"), webdavUrl),
                        ResourceProvider.GetString("LOCSaveManagerMsgConfigSuccess"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information
                    );
                    return true;
                }
                else
                {
                    logger.Error($"Failed to configure {displayName}: {result.Error}");
                    playniteApi.Dialogs.ShowErrorMessage(
                        ResourceProvider.GetString("LOCSaveManagerMsgWebDAVConfigFailed") + $"\n\n{result.Error}",
                        ResourceProvider.GetString("LOCSaveManagerMsgConfigFailed")
                    );
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to configure WebDAV provider");
                return false;
            }
        }

        /// <summary>
        /// 配置 S3 兼容云服务（Cloudflare R2）
        /// </summary>
        private async Task<bool> ConfigureS3ProviderAsync(CloudProvider provider)
        {
            try
            {
                var configName = CloudProviderHelper.GetConfigName(provider);
                var displayName = CloudProviderHelper.GetDisplayName(provider);

                // 提示用户输入 Account ID
                var accountId = playniteApi.Dialogs.SelectString(
                    ResourceProvider.GetString("LOCSaveManagerMsgEnterR2AccountId"),
                    displayName,
                    ""
                );

                if (!accountId.Result || string.IsNullOrWhiteSpace(accountId.SelectedString))
                {
                    logger.Info("User cancelled R2 Account ID input");
                    return false;
                }

                // 提示用户输入 Access Key ID
                var accessKey = playniteApi.Dialogs.SelectString(
                    ResourceProvider.GetString("LOCSaveManagerMsgEnterR2AccessKey"),
                    displayName,
                    ""
                );

                if (!accessKey.Result || string.IsNullOrWhiteSpace(accessKey.SelectedString))
                {
                    logger.Info("User cancelled R2 Access Key input");
                    return false;
                }

                // 提示用户输入 Secret Access Key
                var secretKey = playniteApi.Dialogs.SelectString(
                    ResourceProvider.GetString("LOCSaveManagerMsgEnterR2SecretKey"),
                    displayName,
                    ""
                );

                if (!secretKey.Result || string.IsNullOrWhiteSpace(secretKey.SelectedString))
                {
                    logger.Info("User cancelled R2 Secret Key input");
                    return false;
                }

                // 提示用户输入 Bucket 名称
                var bucketName = playniteApi.Dialogs.SelectString(
                    ResourceProvider.GetString("LOCSaveManagerMsgEnterR2Bucket"),
                    displayName,
                    "playnite-saves"
                );

                if (!bucketName.Result || string.IsNullOrWhiteSpace(bucketName.SelectedString))
                {
                    logger.Info("User cancelled R2 Bucket name input");
                    return false;
                }

                // 构建 Cloudflare R2 endpoint
                var endpoint = $"https://{accountId.SelectedString.Trim()}.r2.cloudflarestorage.com";

                // 使用 rclone obscure 加密 secret key
                var obscureResult = await ExecuteRcloneCommandAsync(
                    $"obscure \"{secretKey.SelectedString}\"",
                    TimeSpan.FromSeconds(10)
                );

                if (!obscureResult.Success)
                {
                    logger.Error($"Failed to obscure secret key: {obscureResult.Error}");
                    playniteApi.Dialogs.ShowErrorMessage(
                        $"密钥加密失败：{obscureResult.Error}",
                        "配置失败"
                    );
                    return false;
                }

                var obscuredSecret = obscureResult.Output.Trim();

                // 使用 rclone config create 创建 S3 配置
                // Cloudflare R2 需要特定的 provider 和 region 设置
                var result = await ExecuteRcloneCommandAsync(
                    $"config create {configName} s3 " +
                    $"provider Cloudflare " +
                    $"access_key_id \"{accessKey.SelectedString.Trim()}\" " +
                    $"secret_access_key \"{obscuredSecret}\" " +
                    $"endpoint \"{endpoint}\" " +
                    $"acl private " +
                    $"--config \"{RcloneConfigPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                if (result.Success && IsConfigured(provider))
                {
                    logger.Info($"{displayName} configured successfully");
                    
                    // 保存 bucket 名称到设置（通过更新 RemoteRootFolder 使用 bucket）
                    // 注意：R2 需要在路径前添加 bucket 名称
                    // 这里我们使用一个特殊的方式：将 bucket 名称写入配置文件
                    await SaveR2BucketConfigAsync(bucketName.SelectedString.Trim());
                    
                    // 创建 SaveManager 根目录
                    await EnsureRemoteRootDirectoryAsync(provider);
                    
                    playniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgR2ConfigSuccess"), bucketName.SelectedString.Trim()),
                        ResourceProvider.GetString("LOCSaveManagerMsgConfigSuccess"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information
                    );
                    return true;
                }
                else
                {
                    logger.Error($"Failed to configure {displayName}: {result.Error}");
                    playniteApi.Dialogs.ShowErrorMessage(
                        $"Cloudflare R2 配置失败：{result.Error}\n\n请检查 Account ID、Access Key 和 Secret Key 是否正确。",
                        "配置失败"
                    );
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to configure S3 provider");
                return false;
            }
        }

        /// <summary>
        /// 保存 R2 bucket 配置
        /// </summary>
        private async Task SaveR2BucketConfigAsync(string bucketName)
        {
            try
            {
                var r2ConfigPath = Path.Combine(dataPath, "r2_config.json");
                var config = new { Bucket = bucketName };
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(config);
                File.WriteAllText(r2ConfigPath, json);
                logger.Info($"R2 bucket config saved: {bucketName}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to save R2 bucket config");
            }
        }

        /// <summary>
        /// 获取 R2 bucket 名称
        /// </summary>
        private string GetR2BucketName()
        {
            try
            {
                var r2ConfigPath = Path.Combine(dataPath, "r2_config.json");
                if (File.Exists(r2ConfigPath))
                {
                    var json = File.ReadAllText(r2ConfigPath);
                    dynamic config = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                    return config?.Bucket ?? "playnite-saves";
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to read R2 bucket config");
            }
            return "playnite-saves";
        }

        /// <summary>
        /// 测试云服务连接
        /// </summary>
        public async Task<bool> TestConnectionAsync(CloudProvider provider)
        {
            try
            {
                var configName = CloudProviderHelper.GetConfigName(provider);

                var result = await ExecuteRcloneCommandAsync(
                    $"lsd {configName}: --max-depth 1 --config \"{RcloneConfigPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                return result.Success;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Connection test failed");
                return false;
            }
        }

        #endregion

        #region 上传操作

        /// <summary>
        /// 上传文件到云端
        /// </summary>
        public async Task<bool> UploadFileAsync(
            string localPath,
            string remotePath,
            CloudProvider provider,
            CancellationToken cancellationToken = default)
        {
            var configName = CloudProviderHelper.GetConfigName(provider);
            var fullRemotePath = $"{configName}:{GetFullRemoteRootPath(provider)}/{remotePath}";

            // 确保父目录存在（对于 WebDAV 特别重要）
            var parentDir = Path.GetDirectoryName(remotePath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(parentDir))
            {
                await EnsureRemoteDirectoryAsync(parentDir, provider);
            }

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                if (cancellationToken.IsCancellationRequested) return false;

                try
                {
                    logger.Debug($"Upload attempt {attempt}/{MaxRetries}: {Path.GetFileName(localPath)}");

                    var result = await ExecuteRcloneCommandAsync(
                        $"copyto \"{localPath}\" \"{fullRemotePath}\" --config \"{RcloneConfigPath}\"",
                        ProcessTimeout
                    );

                    if (result.Success)
                    {
                        logger.Info($"Uploaded: {Path.GetFileName(localPath)}");
                        return true;
                    }

                    logger.Warn($"Upload attempt {attempt} failed: {result.Error}");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Upload attempt {attempt} exception");
                }

                if (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                }
            }

            return false;
        }

        /// <summary>
        /// 上传目录到云端
        /// </summary>
        public async Task<bool> UploadDirectoryAsync(
            string localDir,
            string remotePath,
            CloudProvider provider,
            CancellationToken cancellationToken = default)
        {
            var configName = CloudProviderHelper.GetConfigName(provider);
            var fullRemotePath = $"{configName}:{GetFullRemoteRootPath(provider)}/{remotePath}";

            try
            {
                logger.Info($"Uploading directory: {localDir} -> {fullRemotePath}");

                var result = await ExecuteRcloneCommandAsync(
                    $"copy \"{localDir}\" \"{fullRemotePath}\" --config \"{RcloneConfigPath}\"",
                    TimeSpan.FromMinutes(30)
                );

                if (result.Success)
                {
                    logger.Info($"Directory uploaded successfully");
                    return true;
                }

                logger.Error($"Directory upload failed: {result.Error}");
                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Directory upload exception");
                return false;
            }
        }

        #endregion

        #region 删除操作

        /// <summary>
        /// 删除云端单个文件
        /// </summary>
        public async Task<bool> DeleteRemoteFileAsync(string remotePath, CloudProvider provider)
        {
            var configName = CloudProviderHelper.GetConfigName(provider);
            var fullRemotePath = $"{configName}:{GetFullRemoteRootPath(provider)}/{remotePath}";

            try
            {
                logger.Info($"Deleting remote file: {remotePath}");

                var result = await ExecuteRcloneCommandAsync(
                    $"deletefile \"{fullRemotePath}\" --config \"{RcloneConfigPath}\"",
                    ProcessTimeout
                );

                if (result.Success)
                {
                    logger.Info($"Remote file deleted: {remotePath}");
                    return true;
                }

                // 如果文件不存在，也视为成功
                if (IsNotFoundError(result))
                {
                    logger.Info($"Remote file not found (already deleted): {remotePath}");
                    return true;
                }

                logger.Warn($"Failed to delete remote file: {result.Error}");
                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Exception deleting remote file: {remotePath}");
                return false;
            }
        }

        /// <summary>
        /// 列出远程目录中的所有文件（递归）
        /// </summary>
        public async Task<List<string>> ListRemoteFilesAsync(string remotePath, CloudProvider provider)
        {
            var configName = CloudProviderHelper.GetConfigName(provider);
            var fullRemotePath = $"{configName}:{GetFullRemoteRootPath(provider)}/{remotePath}";
            var files = new List<string>();

            try
            {
                logger.Info($"Listing remote files: {remotePath}");

                var result = await ExecuteRcloneCommandAsync(
                    $"lsjson \"{fullRemotePath}\" --recursive --files-only --config \"{RcloneConfigPath}\"",
                    ProcessTimeout
                );

                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    try
                    {
                        var jsonArray = JArray.Parse(result.Output);
                        foreach (var item in jsonArray)
                        {
                            var path = item["Path"]?.ToString();
                            if (!string.IsNullOrEmpty(path))
                            {
                                files.Add(path);
                            }
                        }
                        logger.Info($"Found {files.Count} files in {remotePath}");
                    }
                    catch (Exception parseEx)
                    {
                        logger.Warn(parseEx, "Failed to parse lsjson output");
                    }
                }
                else if (IsNotFoundError(result))
                {
                    logger.Info($"Remote directory not found: {remotePath}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Exception listing remote files: {remotePath}");
            }

            return files;
        }

        /// <summary>
        /// 删除远程目录（整个文件夹）
        /// </summary>
        public async Task<bool> DeleteRemoteDirectoryAsync(string remotePath, CloudProvider provider)
        {
            var configName = CloudProviderHelper.GetConfigName(provider);
            var fullRemotePath = $"{configName}:{GetFullRemoteRootPath(provider)}/{remotePath}";

            try
            {
                logger.Info($"Deleting remote directory: {remotePath}");

                // 使用 purge 命令删除整个目录
                var result = await ExecuteRcloneCommandAsync(
                    $"purge \"{fullRemotePath}\" --config \"{RcloneConfigPath}\"",
                    TimeSpan.FromMinutes(10)  // 给更长的超时时间
                );

                if (result.Success)
                {
                    logger.Info($"Remote directory deleted: {remotePath}");
                    return true;
                }

                // 如果目录不存在，也视为成功
                if (IsNotFoundError(result))
                {
                    logger.Info($"Remote directory not found (already deleted): {remotePath}");
                    return true;
                }

                logger.Warn($"Failed to delete remote directory: {result.Error}");
                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Exception deleting remote directory: {remotePath}");
                return false;
            }
        }

        #endregion

        #region 下载操作


        /// <summary>
        /// 从云端下载文件
        /// </summary>
        public async Task<bool> DownloadFileAsync(
            string remotePath,
            string localPath,
            CloudProvider provider,
            CancellationToken cancellationToken = default)
        {
            var configName = CloudProviderHelper.GetConfigName(provider);
            var fullRemotePath = $"{configName}:{GetFullRemoteRootPath(provider)}/{remotePath}";

            // 确保目标目录存在
            var targetDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                if (cancellationToken.IsCancellationRequested) return false;

                try
                {
                    logger.Debug($"Download attempt {attempt}/{MaxRetries}: {remotePath}");

                    var result = await ExecuteRcloneCommandAsync(
                        $"copyto \"{fullRemotePath}\" \"{localPath}\" --config \"{RcloneConfigPath}\"",
                        ProcessTimeout
                    );

                    if (result.Success)
                    {
                        logger.Info($"Downloaded: {remotePath}");
                        return true;
                    }

                    logger.Warn($"Download attempt {attempt} failed: {result.Error}");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Download attempt {attempt} exception");
                }

                if (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                }
            }

            return false;
        }

        /// <summary>
        /// 从云端下载目录
        /// </summary>
        /// <param name="remotePath">远程路径</param>
        /// <param name="localDir">本地目录</param>
        /// <param name="provider">云服务提供商</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="excludePattern">排除模式（可选），如 "*/Latest.zip"</param>
        public async Task<bool> DownloadDirectoryAsync(
            string remotePath,
            string localDir,
            CloudProvider provider,
            CancellationToken cancellationToken = default,
            string excludePattern = null)
        {
            var configName = CloudProviderHelper.GetConfigName(provider);
            var fullRemotePath = $"{configName}:{GetFullRemoteRootPath(provider)}/{remotePath}";

            try
            {
                // 确保目标目录存在
                Directory.CreateDirectory(localDir);

                logger.Info($"Downloading directory: {fullRemotePath} -> {localDir}");

                // 构建命令
                var command = $"copy \"{fullRemotePath}\" \"{localDir}\" --config \"{RcloneConfigPath}\"";
                if (!string.IsNullOrEmpty(excludePattern))
                {
                    command += $" --exclude \"{excludePattern}\"";
                    logger.Info($"Excluding pattern: {excludePattern}");
                }

                var result = await ExecuteRcloneCommandAsync(command, TimeSpan.FromMinutes(30));

                if (result.Success)
                {
                    logger.Info($"Directory downloaded successfully");
                    return true;
                }

                logger.Error($"Directory download failed: {result.Error}");
                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Directory download exception");
                return false;
            }
        }

        /// <summary>
        /// 检查远程文件是否存在
        /// 返回：true=存在，false=不存在，null=连接失败/超时
        /// </summary>
        public async Task<bool?> RemoteFileExistsAsync(string remotePath, CloudProvider provider)
        {
            var configName = CloudProviderHelper.GetConfigName(provider);
            var fullRemotePath = $"{configName}:{GetFullRemoteRootPath(provider)}/{remotePath}";

            try
            {
                var result = await ExecuteRcloneCommandAsync(
                    $"lsf \"{fullRemotePath}\" --config \"{RcloneConfigPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                if (!result.Success)
                {
                    // 检查是否是"不存在"还是"连接失败"
                    var errorLower = (result.Error ?? "").ToLower();
                    if (errorLower.Contains("directory not found") || 
                        errorLower.Contains("object not found") ||
                        errorLower.Contains("file not found") ||
                        errorLower.Contains("not found") ||
                        errorLower.Contains("404"))
                    {
                        // 文件/目录不存在
                        return false;
                    }
                    // 其他错误（超时、网络问题等）
                    logger.Warn($"Cloud connection error when checking {remotePath}: {result.Error}");
                    return null;
                }

                return !string.IsNullOrWhiteSpace(result.Output);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Exception when checking remote file exists: {remotePath}");
                return null; // 连接错误
            }
        }

        /// <summary>
        /// 检查远程文件是否存在（简化版本，返回 bool）
        /// </summary>
        public async Task<bool> CheckRemoteFileExistsAsync(string remotePath, CloudProvider provider)
        {
            var result = await RemoteFileExistsAsync(remotePath, provider);
            return result == true;
        }

        /// <summary>
        /// 获取远程文件信息（是否存在及文件大小）
        /// </summary>
        public async Task<(bool exists, long size)> GetRemoteFileInfoAsync(string remotePath, CloudProvider provider)
        {
            var configName = CloudProviderHelper.GetConfigName(provider);
            var fullRemotePath = $"{configName}:{GetFullRemoteRootPath(provider)}/{remotePath}";

            try
            {
                // 使用 lsjson 获取文件详细信息
                var result = await ExecuteRcloneCommandAsync(
                    $"lsjson \"{fullRemotePath}\" --config \"{RcloneConfigPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                if (!result.Success)
                {
                    var errorLower = (result.Error ?? "").ToLower();
                    if (errorLower.Contains("directory not found") || 
                        errorLower.Contains("object not found") ||
                        errorLower.Contains("file not found") ||
                        errorLower.Contains("not found") ||
                        errorLower.Contains("404"))
                    {
                        return (false, 0);
                    }
                    logger.Warn($"Cloud connection error when getting file info {remotePath}: {result.Error}");
                    return (false, 0);
                }

                if (string.IsNullOrWhiteSpace(result.Output))
                {
                    return (false, 0);
                }

                // 解析 JSON 输出
                var jsonArray = JArray.Parse(result.Output);
                if (jsonArray.Count == 0)
                {
                    return (false, 0);
                }

                var fileInfo = jsonArray[0];
                var size = fileInfo["Size"]?.Value<long>() ?? 0;
                return (true, size);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Exception when getting remote file info: {remotePath}");
                return (false, 0);
            }
        }

        /// <summary>
        /// 获取远程文件的修改时间
        /// </summary>
        public async Task<DateTime?> GetRemoteFileModTimeAsync(string remotePath, CloudProvider provider)
        {
            var configName = CloudProviderHelper.GetConfigName(provider);
            var fullRemotePath = $"{configName}:{GetFullRemoteRootPath(provider)}/{remotePath}";

            try
            {
                // 使用 lsjson 获取文件信息
                var result = await ExecuteRcloneCommandAsync(
                    $"lsjson \"{fullRemotePath}\" --config \"{RcloneConfigPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                logger.Info($"lsjson result for {remotePath}: Success={result.Success}, Output={result.Output?.Substring(0, Math.Min(200, result.Output?.Length ?? 0))}");

                if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                {
                    var jsonArray = JArray.Parse(result.Output);
                    if (jsonArray.Count > 0)
                    {
                        var modTimeStr = jsonArray[0]["ModTime"]?.ToString();
                        logger.Info($"ModTime string: {modTimeStr}");
                        if (!string.IsNullOrEmpty(modTimeStr) && DateTime.TryParse(modTimeStr, out var modTime))
                        {
                            return modTime;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to get remote file mod time");
            }

            return null;
        }

        /// <summary>
        /// 云服务验证结果
        /// </summary>
        public class CloudVerifyResult
        {
            public bool ConnectionSuccessful { get; set; }
            public bool BackupExists { get; set; }
            public DateTime? BackupModTime { get; set; }
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// 验证云服务连接和备份状态
        /// </summary>
        public async Task<CloudVerifyResult> VerifyCloudServiceAsync(CloudProvider provider, CancellationToken cancellationToken = default)
        {
            var result = new CloudVerifyResult();

            try
            {
                // 1. 测试连接
                var connected = await TestConnectionAsync(provider);
                result.ConnectionSuccessful = connected;

                if (!connected)
                {
                    result.ErrorMessage = "Cannot connect to cloud service";
                    return result;
                }

                if (cancellationToken.IsCancellationRequested) return result;

                // 2. 检查 config.json 是否存在及其修改时间
                var configExists = await RemoteFileExistsAsync("config.json", provider);
                
                if (configExists == null)
                {
                    result.ConnectionSuccessful = false;
                    result.ErrorMessage = "Connection timeout or error while checking config";
                    return result;
                }
                
                result.BackupExists = configExists == true;

                if (configExists == true)
                {
                    result.BackupModTime = await GetRemoteFileModTimeAsync("config.json", provider);
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                logger.Error(ex, "Cloud service verification failed");
            }

            return result;
        }

        #endregion

        #region Rclone 命令执行

        /// <summary>
        /// 命令执行结果
        /// </summary>
        public class RcloneResult
        {
            public bool Success { get; set; }
            public int ExitCode { get; set; }
            public string Output { get; set; }
            public string Error { get; set; }
        }

        /// <summary>
        /// 执行 Rclone 命令
        /// </summary>
        private async Task<RcloneResult> ExecuteRcloneCommandAsync(
            string arguments,
            TimeSpan timeout,
            bool hideWindow = true)
        {
            var result = new RcloneResult();

            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = RcloneExePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = hideWindow,
                        WindowStyle = hideWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
                    };

                    var outputBuilder = new System.Text.StringBuilder();
                    var errorBuilder = new System.Text.StringBuilder();

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null) outputBuilder.AppendLine(e.Data);
                    };
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null) errorBuilder.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds));

                    if (!completed)
                    {
                        process.Kill();
                        result.Error = "Process timed out";
                        result.ExitCode = -1;
                        return result;
                    }

                    result.ExitCode = process.ExitCode;
                    result.Output = outputBuilder.ToString();
                    result.Error = errorBuilder.ToString();
                    result.Success = result.ExitCode == 0;

                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.ExitCode = -1;
                return result;
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 清理游戏名（移除非法字符）
        /// </summary>
        public static string SanitizeGameName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "UnknownGame";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
            sanitized = sanitized.Trim().Trim('.');

            return string.IsNullOrEmpty(sanitized) ? "UnknownGame" : sanitized;
        }

        /// <summary>
        /// 获取游戏在云端的备份目录路径
        /// </summary>
        public string GetRemoteGamePath(Guid configId, string gameName = null)
        {
            // 只使用 ConfigId 作为文件夹名，避免不同设备上游戏名称不一致的问题
            return $"Backups/{configId}";
        }

        /// <summary>
        /// 删除云端所有数据
        /// </summary>
        public async Task<bool> DeleteAllCloudDataAsync(CloudProvider provider)
        {
            try
            {
                var configName = CloudProviderHelper.GetConfigName(provider);
                var remotePath = $"{configName}:{GetFullRemoteRootPath(provider)}";

                logger.Info($"Deleting all cloud data at {remotePath}");

                // 某些 WebDAV 服务（如坚果云）可能不允许直接 purge 根目录
                // 所以我们改为先删除所有文件，再删除所有空目录
                
                // 1. 删除所有文件
                logger.Info($"Deleting all files in {remotePath}");
                var deleteResult = await ExecuteRcloneCommandAsync(
                    $"delete \"{remotePath}\" --config \"{RcloneConfigPath}\"",
                    ProcessTimeout);

                if (!deleteResult.Success && !IsNotFoundError(deleteResult))
                {
                    logger.Warn($"Failed to delete files: {deleteResult.Error}");
                    // 如果删除文件失败，但不是因为找不到目录，则返回失败
                    // 但我们可以继续尝试删除目录，以防万一
                }

                // 2. 删除所有空目录 (包括子目录)
                logger.Info($"Deleting empty directories in {remotePath}");
                var rmdirsResult = await ExecuteRcloneCommandAsync(
                    $"rmdirs \"{remotePath}\" --leave-root --config \"{RcloneConfigPath}\"",
                    ProcessTimeout);

                if (rmdirsResult.Success || IsNotFoundError(rmdirsResult))
                {
                    logger.Info("All cloud data deleted (cleared) successfully");
                    return true;
                }
                else
                {
                     logger.Warn($"Failed to delete directories: {rmdirsResult.Error}");
                     return false;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to delete cloud data");
                return false;
            }
        }

        private bool IsNotFoundError(RcloneResult result)
        {
            return result.Output.Contains("directory not found") || 
                   result.Error.Contains("directory not found") ||
                   result.Output.Contains("not found") || 
                   result.Error.Contains("not found");
        }

        #endregion
    }
}
