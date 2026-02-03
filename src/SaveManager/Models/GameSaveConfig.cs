using System;
using System.Collections.Generic;

namespace SaveManager.Models
{
    /// <summary>
    /// 游戏存档配置 - 存储游戏的存档路径配置
    /// 支持多设备：使用 ConfigId 作为唯一标识，GameIds 存储多台设备的游戏ID
    /// </summary>
    public class GameSaveConfig
    {
        /// <summary>
        /// 配置的唯一标识（跨设备不变）
        /// </summary>
        public Guid ConfigId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 多设备 GameId 列表
        /// 每台设备上的同一游戏可能有不同的 GameId，都会添加到此列表
        /// </summary>
        public List<Guid> GameIds { get; set; } = new List<Guid>();

        /// <summary>
        /// 游戏名称（用于显示和跨设备匹配）
        /// </summary>
        public string GameName { get; set; }

        /// <summary>
        /// 存档路径列表（可以是文件夹或文件）
        /// </summary>
        public List<SavePath> SavePaths { get; set; } = new List<SavePath>();

        /// <summary>
        /// 还原排除路径列表（还原时这些路径将被保留不被覆盖）
        /// </summary>
        public List<SavePath> RestoreExcludePaths { get; set; } = new List<SavePath>();

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        #region 兼容性属性（用于旧版本数据迁移）

        /// <summary>
        /// 旧版单个 GameId（仅用于反序列化兼容，不应在新代码中使用）
        /// </summary>
        [Obsolete("Use GameIds instead. This property is kept for backward compatibility.")]
        public Guid GameId
        {
            get => GameIds.Count > 0 ? GameIds[0] : Guid.Empty;
            set
            {
                if (value != Guid.Empty && !GameIds.Contains(value))
                {
                    GameIds.Insert(0, value);
                }
            }
        }

        #endregion

        /// <summary>
        /// 检查指定的 GameId 是否属于此配置
        /// </summary>
        public bool ContainsGameId(Guid gameId)
        {
            return GameIds.Contains(gameId);
        }

        /// <summary>
        /// 添加新的 GameId 到列表（如果不存在）
        /// </summary>
        public bool AddGameId(Guid gameId)
        {
            if (gameId != Guid.Empty && !GameIds.Contains(gameId))
            {
                GameIds.Add(gameId);
                UpdatedAt = DateTime.Now;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 从列表中移除指定的 GameId
        /// </summary>
        public bool RemoveGameId(Guid gameId)
        {
            if (GameIds.Remove(gameId))
            {
                UpdatedAt = DateTime.Now;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 被用户明确排除的 GameId 列表
        /// 这些 GameId 不会被自动匹配
        /// </summary>
        public List<Guid> ExcludedGameIds { get; set; } = new List<Guid>();

        /// <summary>
        /// 禁用自动匹配标志
        /// 当用户明确清除匹配时设置为 true，防止通过名称自动匹配
        /// </summary>
        public bool DisableAutoMatch { get; set; } = false;

        #region 云同步字段

        /// <summary>
        /// 云端 Latest 备份的 CRC（从云端 config.json 同步）
        /// </summary>
        public string CloudLatestCRC { get; set; }

        /// <summary>
        /// 云端 Latest 备份的版本历史 CRC 列表（从云端 config.json 同步）
        /// </summary>
        public List<string> CloudVersionHistory { get; set; } = new List<string>();

        /// <summary>
        /// 云端 Latest 备份的时间（从云端 config.json 同步）
        /// </summary>
        public DateTime? CloudLatestTime { get; set; }

        /// <summary>
        /// 云端 Latest 备份的大小（从云端 config.json 同步）
        /// </summary>
        public long? CloudLatestSize { get; set; }

        #endregion

        /// <summary>
        /// 添加 GameId 到排除列表
        /// </summary>
        public bool ExcludeGameId(Guid gameId)
        {
            if (gameId != Guid.Empty && !ExcludedGameIds.Contains(gameId))
            {
                ExcludedGameIds.Add(gameId);
                UpdatedAt = DateTime.Now;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 从排除列表中移除指定的 GameId
        /// </summary>
        public bool UnexcludeGameId(Guid gameId)
        {
            if (ExcludedGameIds.Remove(gameId))
            {
                UpdatedAt = DateTime.Now;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 检查指定的 GameId 是否被排除
        /// </summary>
        public bool IsGameIdExcluded(Guid gameId)
        {
            return ExcludedGameIds.Contains(gameId);
        }
    }

    /// <summary>
    /// 存档路径
    /// </summary>
    public class SavePath
    {
        /// <summary>
        /// 路径（绝对路径）
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// 是否为目录
        /// </summary>
        public bool IsDirectory { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName => System.IO.Path.GetFileName(Path);
    }
}
