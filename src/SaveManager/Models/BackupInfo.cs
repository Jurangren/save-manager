using System;
using System.Collections.Generic;

namespace SaveManager.Models
{
    /// <summary>
    /// 备份信息 - 存储在 ZIP 文件内的元数据
    /// </summary>
    public class BackupInfo
    {
        /// <summary>
        /// 备份描述/备注
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 备份创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 原始游戏名称
        /// </summary>
        public string GameName { get; set; }

        /// <summary>
        /// 备份版本（用于未来兼容性）
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// 备份文件的 CRC32 校验值（在创建后计算并更新）
        /// </summary>
        public string CRC { get; set; }

        /// <summary>
        /// 版本历史记录（CRC 列表，用于跨设备同步时判断关系）
        /// </summary>
        public List<string> VersionHistory { get; set; } = new List<string>();
    }
}
