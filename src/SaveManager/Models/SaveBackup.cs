using System;

namespace SaveManager.Models
{
    /// <summary>
    /// 存档备份记录
    /// </summary>
    public class SaveBackup
    {
        /// <summary>
        /// 备份ID
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 关联的游戏ID
        /// </summary>
        public Guid GameId { get; set; }

        /// <summary>
        /// 备份名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 备份描述/备注
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 备份ZIP文件路径
        /// </summary>
        public string BackupFilePath { get; set; }

        /// <summary>
        /// 备份创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// 格式化的文件大小
        /// </summary>
        public string FormattedSize
        {
            get
            {
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = FileSize;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }

        /// <summary>
        /// 格式化的创建时间
        /// </summary>
        public string FormattedDate => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
