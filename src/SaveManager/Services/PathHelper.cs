using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SaveManager.Services
{
    public static class PathHelper
    {
        public const string GameDirVariable = "{GameDir}";
        public const string EmulatorDirVariable = "{EmulatorDir}";

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

        public static string ConvertToStoragePath(string fullPath, string gameDir, bool useGameRelative)
        {
            return ConvertToStoragePath(fullPath, gameDir, null, useGameRelative);
        }

        /// <summary>
        /// 将绝对路径转换为包含变量的存储路径
        /// </summary>
        public static string ConvertToStoragePath(string fullPath, string gameDir, string emulatorDir, bool useGameRelative)
        {
            try
            {
                var absPath = Path.GetFullPath(fullPath);

                // 1. 尝试转换为游戏相对路径
                if (useGameRelative && !string.IsNullOrEmpty(gameDir))
                {
                    // 确保是绝对路径以便处理
                    var absGameDir = Path.GetFullPath(gameDir);

                    if (IsSameDrive(absPath, absGameDir))
                    {
                        var pathUri = new Uri(absPath);
                        // 确保目录以斜杠结尾以便正确计算相对路径
                        if (!absGameDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                        {
                            absGameDir += Path.DirectorySeparatorChar;
                        }
                        var gameUri = new Uri(absGameDir);

                        // 确保路径在游戏目录下或者是子目录
                        // 注意：MakeRelativeUri 如果不在同一目录下行为可能不符合预期，
                        // 但如果是同一个盘符，通常能生成相对路径（例如 ..\..\）
                        // 这里我们倾向于只有当路径在游戏目录内部时才转换，或者是兄弟目录？
                        // 原有逻辑 seems to accept any relative path on same drive?
                        // "IsSameDrive" check above suggests it.

                        var relativeUri = gameUri.MakeRelativeUri(pathUri);
                        var relativePath = Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
                        
                        // 只有当路径看起来像是在游戏目录内或者附近比较合理时？
                        // 如果生成了太多的 ..\.. 可能不太好看，但技术上是可行的。
                        // 这里保留原有逻辑
                        return Path.Combine(GameDirVariable, relativePath);
                    }
                }

                // 2. 尝试转换为模拟器相对路径
                if (!string.IsNullOrEmpty(emulatorDir))
                {
                    var absEmuDir = Path.GetFullPath(emulatorDir);
                    if (IsSameDrive(absPath, absEmuDir))
                    {
                        // 只有当文件在模拟器目录内部时才转换
                        if (absPath.StartsWith(absEmuDir, StringComparison.OrdinalIgnoreCase))
                        {
                            var relative = absPath.Substring(absEmuDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            return Path.Combine(EmulatorDirVariable, relative);
                        }
                    }
                }

                // 3. 转换为系统相对路径（替换环境变量）
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

        public static string ResolvePath(string storagePath, string gameDir)
        {
            return ResolvePath(storagePath, gameDir, null);
        }

        /// <summary>
        /// 将存储路径解析为绝对路径
        /// </summary>
        public static string ResolvePath(string storagePath, string gameDir, string emulatorDir)
        {
            if (string.IsNullOrEmpty(storagePath)) return storagePath;

            try
            {
                var resolved = storagePath;

                // 1. 替换 {GameDir}
                if (resolved.Contains(GameDirVariable))
                {
                    if (string.IsNullOrEmpty(gameDir)) return resolved; 
                    resolved = resolved.Replace(GameDirVariable, gameDir);
                }

                // 2. 替换 {EmulatorDir}
                if (resolved.Contains(EmulatorDirVariable))
                {
                    if (string.IsNullOrEmpty(emulatorDir)) return resolved;
                    resolved = resolved.Replace(EmulatorDirVariable, emulatorDir);
                }

                // 3. 替换环境变量
                resolved = Environment.ExpandEnvironmentVariables(resolved);

                // 4. 规范化路径 (处理 ..\ 等相对符号)
                return Path.GetFullPath(resolved);
            }
            catch
            {
                return storagePath;
            }
        }
    }
}
