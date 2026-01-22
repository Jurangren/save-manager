using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SaveManager.Services
{
    public static class PathHelper
    {
        public const string GameDirVariable = "{GameDir}";

        /// <summary>
        /// 检查两个路径是否在同一个盘符
        /// </summary>
        public static bool IsSameDrive(string path1, string path2)
        {
            if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2)) return false;
            try
            {
                var root1 = Path.GetPathRoot(Path.GetFullPath(path1));
                var root2 = Path.GetPathRoot(Path.GetFullPath(path2));
                return string.Equals(root1, root2, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// 将绝对路径转换为包含变量的存储路径
        /// </summary>
        public static string ConvertToStoragePath(string fullPath, string gameDir, bool useGameRelative)
        {
            try
            {
                // 1. 尝试转换为游戏相对路径
                if (useGameRelative && !string.IsNullOrEmpty(gameDir))
                {
                    // 确保是绝对路径以便处理
                    var absGameDir = Path.GetFullPath(gameDir);
                    var absPath = Path.GetFullPath(fullPath);

                    if (IsSameDrive(absPath, absGameDir))
                    {
                        var pathUri = new Uri(absPath);
                        // 确保目录以斜杠结尾以便正确计算相对路径
                        if (!absGameDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                        {
                            absGameDir += Path.DirectorySeparatorChar;
                        }
                        var gameUri = new Uri(absGameDir);

                        var relativeUri = gameUri.MakeRelativeUri(pathUri);
                        var relativePath = Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
                        
                        return Path.Combine(GameDirVariable, relativePath);
                    }
                }

                // 2. 转换为系统相对路径（替换环境变量）
                // 重点处理用户目录
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile) && fullPath.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = fullPath.Substring(userProfile.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return Path.Combine("%USERPROFILE%", relative);
                }

                return fullPath;
            }
            catch
            {
                return fullPath;
            }
        }

        /// <summary>
        /// 将存储路径解析为绝对路径
        /// </summary>
        public static string ResolvePath(string storagePath, string gameDir)
        {
            if (string.IsNullOrEmpty(storagePath)) return storagePath;

            try
            {
                var resolved = storagePath;

                // 1. 替换 {GameDir}
                if (resolved.Contains(GameDirVariable))
                {
                    if (string.IsNullOrEmpty(gameDir)) return resolved;
                    // 注意：简单的 Replace 可能无法处理 ..\ 的组合，但 Path.GetFullPath 会处理
                    resolved = resolved.Replace(GameDirVariable, gameDir);
                }

                // 2. 替换环境变量
                resolved = Environment.ExpandEnvironmentVariables(resolved);

                // 3. 规范化路径 (处理 ..\ 等相对符号)
                return Path.GetFullPath(resolved);
            }
            catch
            {
                return storagePath;
            }
        }
    }
}
