using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SaveManager.Models
{
    /// <summary>
    /// 存档备份记录
    /// </summary>
    public class SaveBackup : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 备份ID
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 关联的配置ID（新架构，跨设备不变）
        /// </summary>
        public Guid ConfigId { get; set; }

        /// <summary>
        /// 关联的游戏ID（旧架构，保留用于兼容性和本地设备标识）
        /// </summary>
        public Guid GameId { get; set; }

        /// <summary>
        /// 备份名称
        /// </summary>
        public string Name { get; set; }

        private string _description;
        /// <summary>
        /// 备份描述/备注
        /// </summary>
        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged();
                }
            }
        }

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
        /// 是否为自动备份（用于自动清理限制，修改备注后会变为手动备份）
        /// </summary>
        public bool IsAutoBackup { get; set; } = false;

        /// <summary>
        /// 备份文件的 CRC32 校验值（用于完整性验证和版本追踪）
        /// </summary>
        public string CRC { get; set; }

        /// <summary>
        /// 版本历史记录（该游戏所有备份的 CRC 值，按时间顺序）
        /// </summary>
        public List<string> VersionHistory { get; set; } = new List<string>();

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

        /// <summary>
        /// 检查本地备份文件是否存在
        /// 使用 FullPath（如果设置）否则用 BackupFilePath
        /// </summary>
        public bool IsLocalFileExists
        {
            get
            {
                var pathToCheck = !string.IsNullOrEmpty(FullPath) ? FullPath : BackupFilePath;
                return !string.IsNullOrEmpty(pathToCheck) && System.IO.File.Exists(pathToCheck);
            }
        }

        /// <summary>
        /// 完整路径（由 ViewModel 设置，用于检查本地文件是否存在）
        /// 不序列化到文件
        /// </summary>
        [Playnite.SDK.Data.DontSerialize]
        public string FullPath { get; set; }

        /// <summary>
        /// 显示名称（如果是云端备份，前面加云图标）
        /// </summary>
        public string DisplayName => IsLocalFileExists ? Name : $"☁️ {Name}";
    }
}
