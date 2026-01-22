using System;
using System.Collections.Generic;

namespace SaveManager.Models
{
    /// <summary>
    /// 游戏存档配置 - 存储游戏的存档路径配置
    /// </summary>
    public class GameSaveConfig
    {
        /// <summary>
        /// 游戏ID（对应Playnite的Game.Id）
        /// </summary>
        public Guid GameId { get; set; }

        /// <summary>
        /// 游戏名称（用于显示）
        /// </summary>
        public string GameName { get; set; }

        /// <summary>
        /// 存档路径列表（可以是文件夹或文件）
        /// </summary>
        public List<SavePath> SavePaths { get; set; } = new List<SavePath>();

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
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
