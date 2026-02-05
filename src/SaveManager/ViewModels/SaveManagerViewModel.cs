using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Models;
using SaveManager.Models;
using SaveManager.Services;

namespace SaveManager.ViewModels
{
    /// <summary>
    /// 存档管理器视图模型
    /// </summary>
    public class SaveManagerViewModel : INotifyPropertyChanged
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly BackupService backupService;
        private readonly Game game;

        public event PropertyChangedEventHandler PropertyChanged;

        public string GameName => game.Name;
        public string GameId => game.Id.ToString();

        private ObservableCollection<SavePathItem> _savePaths;
        public ObservableCollection<SavePathItem> SavePaths
        {
            get => _savePaths;
            set { _savePaths = value; OnPropertyChanged(); }
        }

        private ObservableCollection<SaveBackup> _backups;
        public ObservableCollection<SaveBackup> Backups
        {
            get => _backups;
            set { _backups = value; OnPropertyChanged(); }
        }

        private SaveBackup _selectedBackup;
        public SaveBackup SelectedBackup
        {
            get => _selectedBackup;
            set 
            { 
                _selectedBackup = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IsBackupSelected));
                OnPropertyChanged(nameof(IsSingleBackupSelected));
            }
        }

        private ObservableCollection<SaveBackup> _selectedBackups = new ObservableCollection<SaveBackup>();
        public ObservableCollection<SaveBackup> SelectedBackups
        {
            get => _selectedBackups;
            set
            {
                _selectedBackups = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBackupSelected));
                OnPropertyChanged(nameof(IsSingleBackupSelected));
            }
        }

        // 兼容性属性
        public bool IsBackupSelected => SelectedBackups != null && SelectedBackups.Count > 0;
        public bool IsSingleBackupSelected => SelectedBackups != null && SelectedBackups.Count == 1;

        private string _newBackupDescription;
        public string NewBackupDescription
        {
            get => _newBackupDescription;
            set { _newBackupDescription = value; OnPropertyChanged(); }
        }

        private bool _hasSavePaths;
        public bool HasSavePaths
        {
            get => _hasSavePaths;
            set { _hasSavePaths = value; OnPropertyChanged(); }
        }

        // 还原排除项
        private ObservableCollection<SavePathItem> _restoreExcludePaths;
        public ObservableCollection<SavePathItem> RestoreExcludePaths
        {
            get => _restoreExcludePaths;
            set { _restoreExcludePaths = value; OnPropertyChanged(); }
        }

        private bool _isExcludeExpanded = false;
        public bool IsExcludeExpanded
        {
            get => _isExcludeExpanded;
            set { _isExcludeExpanded = value; OnPropertyChanged(); }
        }

        // 命令
        public ICommand AddFolderCommand { get; }
        public ICommand AddFileCommand { get; }
        public ICommand RemovePathCommand { get; }
        public ICommand OpenPathCommand { get; }
        public ICommand CreateBackupCommand { get; }
        public ICommand RestoreBackupCommand { get; }
        public ICommand DeleteBackupCommand { get; }
        public ICommand OpenBackupFolderCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand EditBackupNoteCommand { get; }
        public ICommand ImportConfigCommand { get; }
        public ICommand ExportConfigCommand { get; }
        public ICommand ImportBackupCommand { get; }
        public ICommand ForceRestoreBackupCommand { get; }

        // 还原排除项命令
        public ICommand AddExcludeFolderCommand { get; }
        public ICommand AddExcludeFileCommand { get; }
        public ICommand RemoveExcludePathCommand { get; }
        public ICommand ToggleExcludeExpandedCommand { get; }

        private readonly CloudSyncManager cloudSyncManager;
        private readonly RcloneService rcloneService;
        private readonly Func<bool> getCloudSyncEnabled;
        private readonly Func<bool> getRealtimeSyncEnabled;

        public SaveManagerViewModel(Game game, IPlayniteAPI playniteApi, BackupService backupService, 
            CloudSyncManager cloudSyncManager = null, RcloneService rcloneService = null,
            Func<bool> getCloudSyncEnabled = null, Func<bool> getRealtimeSyncEnabled = null)
        {
            this.game = game;
            this.playniteApi = playniteApi;
            this.backupService = backupService;
            this.cloudSyncManager = cloudSyncManager;
            this.rcloneService = rcloneService;
            this.getCloudSyncEnabled = getCloudSyncEnabled;
            this.getRealtimeSyncEnabled = getRealtimeSyncEnabled;

            SavePaths = new ObservableCollection<SavePathItem>();
            RestoreExcludePaths = new ObservableCollection<SavePathItem>();
            Backups = new ObservableCollection<SaveBackup>();
            SelectedBackups = new ObservableCollection<SaveBackup>();

            // 初始化命令
            AddFolderCommand = new RelayCommand(AddFolder);
            AddFileCommand = new RelayCommand(AddFile);
            RemovePathCommand = new RelayCommand<SavePathItem>(RemovePath);
            OpenPathCommand = new RelayCommand<SavePathItem>(OpenPath);
            CreateBackupCommand = new RelayCommand(CreateBackup, () => HasSavePaths);
            RestoreBackupCommand = new RelayCommand(RestoreBackup, () => IsSingleBackupSelected && SelectedBackups.FirstOrDefault()?.Name != "Latest");
            DeleteBackupCommand = new RelayCommand(DeleteBackup, () => IsBackupSelected);
            OpenBackupFolderCommand = new RelayCommand(OpenBackupFolder);
            SaveConfigCommand = new RelayCommand(SaveConfig);
            EditBackupNoteCommand = new RelayCommand(EditBackupNote, () => IsSingleBackupSelected);
            ImportConfigCommand = new RelayCommand(ImportConfig);
            ExportConfigCommand = new RelayCommand(ExportConfig);
            ImportBackupCommand = new RelayCommand(ImportBackup);
            ForceRestoreBackupCommand = new RelayCommand(ForceRestoreBackup, () => IsSingleBackupSelected && SelectedBackups.FirstOrDefault()?.Name != "Latest");

            // 还原排除项命令
            AddExcludeFolderCommand = new RelayCommand(AddExcludeFolder);
            AddExcludeFileCommand = new RelayCommand(AddExcludeFile);
            RemoveExcludePathCommand = new RelayCommand<SavePathItem>(RemoveExcludePath);
            ToggleExcludeExpandedCommand = new RelayCommand(ToggleExcludeExpanded);

            // 加载数据
            LoadData();
        }

        private void LoadData()
        {
            // 加载存档路径配置
            var config = backupService.GetGameConfig(game.Id);
            if (config != null)
            {
                foreach (var path in config.SavePaths)
                {
                    SavePaths.Add(new SavePathItem
                    {
                        Path = path.Path,
                        IsDirectory = path.IsDirectory
                    });
                }

                // 加载还原排除项（兼容旧版本配置，可能没有此字段）
                if (config.RestoreExcludePaths != null)
                {
                    foreach (var path in config.RestoreExcludePaths)
                    {
                        RestoreExcludePaths.Add(new SavePathItem
                        {
                            Path = path.Path,
                            IsDirectory = path.IsDirectory
                        });
                    }
                }
            }

            // 加载备份列表
            var backups = backupService.GetBackups(game.Id);
            foreach (var backup in backups)
            {
                // 设置完整路径（用于检查本地文件是否存在）
                if (!string.IsNullOrEmpty(backup.BackupFilePath))
                {
                    backup.FullPath = backupService.GetFullBackupPath(backup.BackupFilePath);
                }
                Backups.Add(backup);
            }

            UpdateHasSavePaths();
        }

        private void AddFolder()
        {
            try
            {
                // 设置初始目录为游戏安装目录
                string initialDir = "";
                if (!string.IsNullOrEmpty(game.InstallDirectory))
                {
                    var normalizedPath = game.InstallDirectory.Replace('/', '\\').TrimEnd('\\');
                    if (Directory.Exists(normalizedPath))
                    {
                        initialDir = normalizedPath;
                    }
                }

                // 使用 Playnite SDK 的文件夹选择对话框
                var selectedPath = playniteApi.Dialogs.SelectFolder();
                
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    ProcessSelectedPath(selectedPath, true);
                }
            }
            catch (Exception ex)
            {
                playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        private void AddFile()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = ResourceProvider.GetString("LOCSaveManagerDialogSelectFile"),
                    Filter = "All files|*.*",
                    Multiselect = true, // 允许选择多个文件
                    CheckFileExists = true
                };

                // 尝试设置初始目录
                if (!string.IsNullOrEmpty(game.InstallDirectory))
                {
                    var normalizedPath = game.InstallDirectory.Replace('/', '\\').TrimEnd('\\');
                    if (Directory.Exists(normalizedPath))
                    {
                        dialog.InitialDirectory = normalizedPath;
                    }
                }

                // 获取主窗口作为父窗口
                var window = playniteApi.Dialogs.GetCurrentAppWindow();
                
                if (dialog.ShowDialog(window) == true)
                {
                    foreach (var fileName in dialog.FileNames)
                    {
                        ProcessSelectedPath(fileName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                 playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        private void ProcessSelectedPath(string selectedPath, bool isDirectory)
        {
            // 自动判断路径类型
            bool useGameRelative = false;
            
            // 如果存档路径在游戏目录下，使用游戏相对路径
            if (!string.IsNullOrEmpty(game.InstallDirectory))
            {
                var normalizedGameDir = Path.GetFullPath(game.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var normalizedPath = Path.GetFullPath(selectedPath);
                
                // 检查 selectedPath 是否以 gameDir 开头（即在游戏目录下）
                useGameRelative = normalizedPath.StartsWith(normalizedGameDir, StringComparison.OrdinalIgnoreCase);
            }

            // 2. 转换路径
            var finalPath = PathHelper.ConvertToStoragePath(selectedPath, game.InstallDirectory, useGameRelative);

            // 3. 查重
            if (SavePaths.Any(p => p.Path.Equals(finalPath, StringComparison.OrdinalIgnoreCase)))
            {
                playniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgPathExists"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 4. 添加
            SavePaths.Add(new SavePathItem
            {
                Path = finalPath,
                IsDirectory = isDirectory
            });
            UpdateHasSavePaths();
            
            // 自动保存配置
            SaveConfigSilent();
        }

        private void ImportConfig()
        {
            var path = playniteApi.Dialogs.SelectFile(ResourceProvider.GetString("LOCSaveManagerDialogImportConfig"));
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                // 尝试解析 JSON。这里假设导入的是 GameSaveConfig 结构，或者包含 SavePaths 的结构
                // 为了通用性，我们可以尝试解析为 GameSaveConfig
                var config = Playnite.SDK.Data.Serialization.FromJson<GameSaveConfig>(json);
                
                if (config != null && config.SavePaths != null)
                {
                    var result = playniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgImportConfirm"), config.SavePaths.Count),
                        ResourceProvider.GetString("LOCSaveManagerTitleConfirmImport"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        SavePaths.Clear();
                        foreach (var p in config.SavePaths)
                        {
                            SavePaths.Add(new SavePathItem { Path = p.Path, IsDirectory = p.IsDirectory });
                        }

                        // 导入还原排除项（兼容旧配置，可能没有此字段）
                        RestoreExcludePaths.Clear();
                        if (config.RestoreExcludePaths != null)
                        {
                            foreach (var p in config.RestoreExcludePaths)
                            {
                                RestoreExcludePaths.Add(new SavePathItem { Path = p.Path, IsDirectory = p.IsDirectory });
                            }
                        }

                        UpdateHasSavePaths();
                        SaveConfigSilent();
                        playniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgImportSuccess"), "Save Manager");
                    }
                }
                else
                {
                    playniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgInvalidConfig"), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        private void ExportConfig()
        {
            if (!HasSavePaths)
            {
                playniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgExportNoPaths"), "Save Manager");
                return;
            }

            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = ResourceProvider.GetString("LOCSaveManagerDialogExportConfig"),
                    Filter = ResourceProvider.GetString("LOCSaveManagerDialogImportConfig"),
                    FileName = $"{game.Name}_SaveConfig.json"
                };

                // 获取主窗口作为父窗口
                var window = playniteApi.Dialogs.GetCurrentAppWindow();

                if (dialog.ShowDialog(window) == true)
                {
                    var config = new GameSaveConfig
                    {
                        GameName = game.Name,
                        SavePaths = new System.Collections.Generic.List<SavePath>(),
                        RestoreExcludePaths = new System.Collections.Generic.List<SavePath>()
                    };
                    config.GameIds.Add(game.Id);

                    foreach (var item in SavePaths)
                    {
                        config.SavePaths.Add(new SavePath
                        {
                            Path = item.Path,
                            IsDirectory = item.IsDirectory
                        });
                    }

                    // 导出还原排除项
                    foreach (var item in RestoreExcludePaths)
                    {
                        config.RestoreExcludePaths.Add(new SavePath
                        {
                            Path = item.Path,
                            IsDirectory = item.IsDirectory
                        });
                    }

                    var json = Playnite.SDK.Data.Serialization.ToJson(config, true);
                    File.WriteAllText(dialog.FileName, json);

                    playniteApi.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString("LOCSaveManagerMsgExportSuccess"), dialog.FileName), "Save Manager");
                }
            }
            catch (Exception ex)
            {
                playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        private void ImportBackup()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = ResourceProvider.GetString("LOCSaveManagerDialogImportBackup"),
                Filter = "Backup File (*.zip)|*.zip",
                Multiselect = true
            };

            var window = playniteApi.Dialogs.GetCurrentAppWindow();
            if (dialog.ShowDialog(window) != true) return;

            try
            {
                int successCount = 0;
                SaveBackup lastBackup = null;

                foreach (var path in dialog.FileNames)
                {
                    try
                    {
                        var backup = backupService.ImportBackup(game.Id, game.Name, path);
                        Backups.Insert(0, backup);
                        lastBackup = backup;
                        successCount++;
                    }
                    catch (Exception innerEx)
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"Import failed for {Path.GetFileName(path)}: {innerEx.Message}", "Import Error");
                    }
                }

                if (successCount == 1 && lastBackup != null)
                {
                    playniteApi.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString("LOCSaveManagerMsgImportBackupSuccess"), lastBackup.Name), "Save Manager");
                }
                else if (successCount > 1)
                {
                    playniteApi.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString("LOCSaveManagerMsgImportMultipleBackupsSuccess"), successCount), "Save Manager");
                }
            }
            catch (Exception ex)
            {
                 playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        private void RemovePath(SavePathItem item)
        {
            if (item != null)
            {
                SavePaths.Remove(item);
                UpdateHasSavePaths();
                
                // 自动保存配置
                SaveConfigSilent();
            }
        }

        private void OpenPath(SavePathItem item)
        {
            if (item != null && item.IsDirectory)
            {
                try
                {
                    var resolvedPath = PathHelper.ResolvePath(item.Path, game.InstallDirectory);
                    if (Directory.Exists(resolvedPath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", resolvedPath);
                    }
                    else
                    {
                        playniteApi.Dialogs.ShowErrorMessage(
                            ResourceProvider.GetString("LOCSaveManagerMsgFolderNotFound"),
                            ResourceProvider.GetString("LOCSaveManagerError"));
                    }
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage(
                        $"Failed to open folder: {ex.Message}",
                        ResourceProvider.GetString("LOCSaveManagerError"));
                }
            }
        }

        private void UpdateHasSavePaths()
        {
            HasSavePaths = SavePaths.Count > 0;
        }

        // 还原排除项相关方法
        private void ToggleExcludeExpanded()
        {
            IsExcludeExpanded = !IsExcludeExpanded;
        }

        private void AddExcludeFolder()
        {
            try
            {
                var selectedPath = playniteApi.Dialogs.SelectFolder();
                
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    ProcessExcludePath(selectedPath, true);
                }
            }
            catch (Exception ex)
            {
                playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        private void AddExcludeFile()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = ResourceProvider.GetString("LOCSaveManagerDialogSelectFile"),
                    Filter = "All files|*.*",
                    Multiselect = true,
                    CheckFileExists = true
                };

                // 尝试设置初始目录
                if (!string.IsNullOrEmpty(game.InstallDirectory))
                {
                    var normalizedPath = game.InstallDirectory.Replace('/', '\\').TrimEnd('\\');
                    if (Directory.Exists(normalizedPath))
                    {
                        dialog.InitialDirectory = normalizedPath;
                    }
                }

                var window = playniteApi.Dialogs.GetCurrentAppWindow();
                
                if (dialog.ShowDialog(window) == true)
                {
                    foreach (var fileName in dialog.FileNames)
                    {
                        ProcessExcludePath(fileName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        private void ProcessExcludePath(string selectedPath, bool isDirectory)
        {
            // 自动判断路径类型
            bool useGameRelative = false;
            
            // 如果路径在游戏目录下，使用游戏相对路径
            if (!string.IsNullOrEmpty(game.InstallDirectory))
            {
                var normalizedGameDir = Path.GetFullPath(game.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var normalizedPath = Path.GetFullPath(selectedPath);
                
                useGameRelative = normalizedPath.StartsWith(normalizedGameDir, StringComparison.OrdinalIgnoreCase);
            }

            // 转换路径
            var finalPath = PathHelper.ConvertToStoragePath(selectedPath, game.InstallDirectory, useGameRelative);

            // 查重
            if (RestoreExcludePaths.Any(p => p.Path.Equals(finalPath, StringComparison.OrdinalIgnoreCase)))
            {
                playniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgPathExists"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 添加
            RestoreExcludePaths.Add(new SavePathItem
            {
                Path = finalPath,
                IsDirectory = isDirectory
            });
            
            // 自动保存配置
            SaveConfigSilent();
        }

        private void RemoveExcludePath(SavePathItem item)
        {
            if (item != null)
            {
                RestoreExcludePaths.Remove(item);
                
                // 自动保存配置
                SaveConfigSilent();
            }
        }

        private void SaveConfig()
        {
            SaveConfigSilent();
            playniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgConfigSaved"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveConfigSilent()
        {
            // 获取或创建配置（保留现有的 ConfigId 和 GameIds）
            var existingConfig = backupService.GetGameConfig(game.Id);
            var config = existingConfig ?? new GameSaveConfig();
            
            // 确保当前游戏 ID 在列表中
            if (!config.GameIds.Contains(game.Id))
            {
                config.GameIds.Add(game.Id);
            }
            
            config.GameName = game.Name;
            config.SavePaths = SavePaths.Select(p => new SavePath
            {
                Path = p.Path,
                IsDirectory = p.IsDirectory
            }).ToList();
            config.RestoreExcludePaths = RestoreExcludePaths.Select(p => new SavePath
            {
                Path = p.Path,
                IsDirectory = p.IsDirectory
            }).ToList();

            backupService.SaveGameConfig(config);

            // 如果启用了云同步，同步配置到云端
            if (cloudSyncManager != null && (getCloudSyncEnabled?.Invoke() ?? false))
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await cloudSyncManager.UploadConfigToCloudAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to upload config after save: {ex.Message}");
                    }
                });
            }
        }

        private void CreateBackup()
        {
            if (!HasSavePaths)
            {
                playniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgConfigRequired"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 弹出对话框让用户输入备注
            var noteResult = playniteApi.Dialogs.SelectString(
                ResourceProvider.GetString("LOCSaveManagerMsgEnterNote"),
                ResourceProvider.GetString("LOCSaveManagerTitleBackupNote"),
                "");

            if (!noteResult.Result)
            {
                return;
            }

            try
            {
                SaveBackup backup = null;
                
                // 使用进度窗口创建备份
                playniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgCreatingBackup");
                    progressArgs.IsIndeterminate = true;
                    backup = backupService.CreateBackup(game.Id, game.Name, noteResult.SelectedString);
                }, new Playnite.SDK.GlobalProgressOptions(
                    ResourceProvider.GetString("LOCSaveManagerMsgCreatingBackup"), false)
                {
                    IsIndeterminate = true
                });

                if (backup == null)
                {
                    return;
                }

                Backups.Insert(0, backup);
                
                // 如果启用了云同步，直接进入同步流程
                if (cloudSyncManager != null && (getCloudSyncEnabled?.Invoke() ?? false))
                {
                    SyncBackupWithBackgroundOption(backup);
                }
                else
                {
                    playniteApi.Dialogs.ShowMessage(
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgBackupSuccess"), backup.Name, backup.FormattedSize), 
                        "Save Manager", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
            }
        }

        private void ForceRestoreBackup()
        {
            var backup = SelectedBackups.FirstOrDefault();
            if (backup == null) return;

            // 确认还原（强制）
            var result = playniteApi.Dialogs.ShowMessage(
                ResourceProvider.GetString("LOCSaveManagerMsgConfirmForceRestore"), 
                ResourceProvider.GetString("LOCSaveManagerTitleConfirmRestore"), 
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 检查本地文件是否存在
                    if (!backup.IsLocalFileExists)
                    {
                        // 需要先从云端下载
                        if (!DownloadBackupFromCloud(backup))
                        {
                            return; // 下载失败
                        }
                    }

                    // 传递 null 以忽略排除项
                    // 使用进度窗口还原备份
                    playniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                    {
                        progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgRestoringBackup");
                        progressArgs.IsIndeterminate = true;
                        backupService.RestoreBackup(backup, null);
                    }, new Playnite.SDK.GlobalProgressOptions(
                        ResourceProvider.GetString("LOCSaveManagerMsgRestoringBackup"), false)
                    {
                        IsIndeterminate = true
                    });

                    playniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgRestoreSuccess"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
                }
            }
        }

        private void RestoreBackup()
        {
            var backup = SelectedBackups.FirstOrDefault();
            if (backup == null) return;

            // 确认还原
            var result = playniteApi.Dialogs.ShowMessage(
                string.Format(ResourceProvider.GetString("LOCSaveManagerMsgConfirmRestoreNamed"), backup.Name),
                ResourceProvider.GetString("LOCSaveManagerTitleConfirmRestore"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 检查本地文件是否存在
                    if (!backup.IsLocalFileExists)
                    {
                        // 需要先从云端下载
                        if (!DownloadBackupFromCloud(backup))
                        {
                            return; // 下载失败
                        }
                    }

                    // 构建排除项列表
                    var excludePaths = RestoreExcludePaths.Select(p => new SavePath
                    {
                        Path = p.Path,
                        IsDirectory = p.IsDirectory
                    }).ToList();

                    // 使用进度窗口还原备份
                    playniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                    {
                        progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgRestoringBackup");
                        progressArgs.IsIndeterminate = true;
                        backupService.RestoreBackup(backup, excludePaths.Count > 0 ? excludePaths : null);
                    }, new Playnite.SDK.GlobalProgressOptions(
                        ResourceProvider.GetString("LOCSaveManagerMsgRestoringBackup"), false)
                    {
                        IsIndeterminate = true
                    });

                    playniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgRestoreSuccess"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
                }
            }
        }

        /// <summary>
        /// 从云端下载备份文件
        /// </summary>
        private bool DownloadBackupFromCloud(SaveBackup backup)
        {
            if (rcloneService == null || !rcloneService.IsRcloneInstalled)
            {
                playniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgRcloneNotInstalled"),
                    "Error");
                return false;
            }

            var config = backupService.GetConfigByConfigId(backup.ConfigId);
            if (config == null)
            {
                playniteApi.Dialogs.ShowErrorMessage("Config not found", "Error");
                return false;
            }

            bool downloaded = false;

            playniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgDownloadingBackup");
                progressArgs.IsIndeterminate = true;

                try
                {
                    var provider = cloudSyncManager?.GetCloudProvider?.Invoke() ?? CloudProvider.GoogleDrive;
                    var remoteGamePath = rcloneService.GetRemoteGamePath(config.ConfigId, config.GameName);
                    var remoteBackupPath = $"{remoteGamePath}/{backup.Name}.zip";
                    var localBackupPath = backupService.GetFullBackupPath(backup.BackupFilePath);

                    // 确保本地目录存在
                    var localDir = System.IO.Path.GetDirectoryName(localBackupPath);
                    if (!Directory.Exists(localDir))
                    {
                        Directory.CreateDirectory(localDir);
                    }

                    var task = rcloneService.DownloadFileAsync(remoteBackupPath, localBackupPath, provider);
                    task.Wait();
                    downloaded = task.Result;
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Download failed: {ex.Message}", "Error");
                }
            }, new GlobalProgressOptions(
                ResourceProvider.GetString("LOCSaveManagerMsgDownloadingBackup"), false)
            {
                IsIndeterminate = true
            });

            if (!downloaded)
            {
                playniteApi.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgDownloadBackupFailed"),
                    "Error");
            }
            else
            {
                // 下载成功后更新 FullPath，这样 IsLocalFileExists 会返回 true
                backup.FullPath = backupService.GetFullBackupPath(backup.BackupFilePath);
            }

            return downloaded;
        }

        private void DeleteBackup()
        {
            if (SelectedBackups.Count == 0) return;

            string confirmMsg;
            if (SelectedBackups.Count == 1)
            {
                confirmMsg = string.Format(ResourceProvider.GetString("LOCSaveManagerMsgConfirmDelete"), SelectedBackups[0].Name);
            }
            else
            {
                confirmMsg = string.Format(ResourceProvider.GetString("LOCSaveManagerMsgConfirmDeleteMultiple"), SelectedBackups.Count);
            }

            var result = playniteApi.Dialogs.ShowMessage(
                confirmMsg,
                ResourceProvider.GetString("LOCSaveManagerTitleConfirmDelete"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 创建一个临时列表来遍历，因为 SelectedBackups 在删除过程中可能会改变
                    var backupsToDelete = SelectedBackups.ToList();
                    
                    foreach (var backup in backupsToDelete)
                    {
                        backupService.DeleteBackup(backup);
                        Backups.Remove(backup);
                    }
                    
                    SelectedBackups.Clear();
                    UpdateSelection(new System.Collections.ArrayList());

                    // 如果启用了云同步，询问用户是否从云端删除
                    if (cloudSyncManager != null && (getCloudSyncEnabled?.Invoke() ?? false))
                    {
                        var syncOptions = new List<Playnite.SDK.MessageBoxOption>
                        {
                            new Playnite.SDK.MessageBoxOption(ResourceProvider.GetString("LOCSaveManagerMsgSyncingToCloud"), true, false),
                            new Playnite.SDK.MessageBoxOption(ResourceProvider.GetString("LOCSaveManagerBtnBackgroundSync"), false, false),
                            new Playnite.SDK.MessageBoxOption(ResourceProvider.GetString("LOCSaveManagerBtnCancel"), false, true)
                        };

                        string deleteSuccessMsg = backupsToDelete.Count > 1 
                            ? $"Deleted {backupsToDelete.Count} backups." 
                            : ResourceProvider.GetString("LOCSaveManagerMsgDeleteSuccess");

                        var syncResult = playniteApi.Dialogs.ShowMessage(
                            deleteSuccessMsg + "\n\n" + ResourceProvider.GetString("LOCSaveManagerMsgDeletingFromCloud").Replace("{0}", "").TrimEnd() + "?",
                            "Save Manager",
                            MessageBoxImage.Question,
                            syncOptions);

                        if (syncResult == syncOptions[0])
                        {
                            // 前台同步删除
                            SyncBackupsDeleteForeground(backupsToDelete);
                        }
                        else if (syncResult == syncOptions[1])
                        {
                            // 后台同步删除
                            SyncBackupsDeleteBackground(backupsToDelete);
                            playniteApi.Dialogs.ShowMessage(
                                ResourceProvider.GetString("LOCSaveManagerMsgBackgroundSyncStarted"),
                                "Save Manager",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        string successMsg = backupsToDelete.Count > 1 
                            ? $"Deleted {backupsToDelete.Count} backups." 
                            : ResourceProvider.GetString("LOCSaveManagerMsgDeleteSuccess");
                            
                        playniteApi.Dialogs.ShowMessage(successMsg, "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
                }
            }
        }

        private void EditBackupNote()
        {
            var backup = SelectedBackups.FirstOrDefault();
            if (backup == null) return;

            // 如果是自动备份，先弹出提示
            if (backup.IsAutoBackup)
            {
                playniteApi.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOCSaveManagerMsgAutoBackupNoteWarning"),
                    ResourceProvider.GetString("LOCSaveManagerTitleAutoBackupNoteWarning"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            var result = playniteApi.Dialogs.SelectString(
                ResourceProvider.GetString("LOCSaveManagerMsgEnterNote"),
                ResourceProvider.GetString("LOCSaveManagerTitleEditNote"),
                backup.Description);

            if (result.Result)
            {
                try
                {
                    var newDescription = result.SelectedString;
                    
                    // 更新服务端和 ZIP 文件
                    backupService.UpdateBackupDescription(backup, newDescription);
                    
                    // 更新对象属性（会自动触发 UI 更新，因为 SaveBackup 实现了 INotifyPropertyChanged）
                    backup.Description = newDescription;
                    
                    // 如果是自动备份，UI 也需要更新 IsAutoBackup 状态
                    // （UpdateBackupDescription 已经将其设为 false）
                    if (backup.IsAutoBackup)
                    {
                        backup.IsAutoBackup = false;
                    }
                    
                    playniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgNoteSuccess"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Error");
                }
            }
        }

        private void OpenBackupFolder()
        {
            string backupsPath = null;
            
            // 优先从现有的备份记录中获取目录（应对游戏名称变更或异地迁移的情况）
            if (Backups != null && Backups.Count > 0)
            {
                var latestBackup = Backups[0];
                var fullPath = backupService.GetFullBackupPath(latestBackup.BackupFilePath);
                if (!string.IsNullOrEmpty(fullPath))
                {
                    backupsPath = Path.GetDirectoryName(fullPath);
                }
            }

            // 如果无法从备份获取，或者目录不存在（可能被删除了），尝试回退到标准生成规则
            if (string.IsNullOrEmpty(backupsPath) || !Directory.Exists(backupsPath))
            {
                backupsPath = backupService.GetGameBackupDirectoryByGameId(game.Id, game.Name);
            }
            
            // 尝试创建目录（如果不存在），方便用户手动管理
            if (!Directory.Exists(backupsPath))
            {
                try
                {
                    Directory.CreateDirectory(backupsPath);
                }
                catch
                {
                    // Ignore creation errors
                }
            }

            if (Directory.Exists(backupsPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", backupsPath);
            }
            else
            {
                playniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSaveManagerMsgNoBackupsFound"), "Save Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 更新选中项 (由 View 调用)
        /// </summary>
        public void UpdateSelection(System.Collections.IList items)
        {
            SelectedBackups.Clear();
            foreach (SaveBackup item in items)
            {
                SelectedBackups.Add(item);
            }
            
            // 确保 SelectedBackup 与第一个选中项同步（为了兼容性）
            _selectedBackup = SelectedBackups.Count > 0 ? SelectedBackups[0] : null;
            OnPropertyChanged(nameof(SelectedBackup));
            
            OnPropertyChanged(nameof(IsBackupSelected));
            OnPropertyChanged(nameof(IsSingleBackupSelected));
            
            // 刷新命令状态
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        #region 云同步辅助方法

        /// <summary>
        /// 前台同步备份到云端（上传或删除）
        /// </summary>
        private void SyncBackupToCloudForeground(SaveBackup backup, bool isUpload)
        {
            bool success = false;
            playniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                progressArgs.IsIndeterminate = true;
                progressArgs.Text = isUpload 
                    ? string.Format(ResourceProvider.GetString("LOCSaveManagerMsgUploadingToCloud"), backup.Name)
                    : string.Format(ResourceProvider.GetString("LOCSaveManagerMsgDeletingFromCloud"), backup.Name);

                var task = isUpload 
                    ? cloudSyncManager.UploadBackupToCloudAsync(backup, game.Name)
                    : cloudSyncManager.DeleteBackupFromCloudAsync(backup);
                task.Wait();
                success = task.Result;
            }, new Playnite.SDK.GlobalProgressOptions(
                ResourceProvider.GetString("LOCSaveManagerMsgSyncingToCloud"), false)
            {
                IsIndeterminate = true
            });

            if (!success)
            {
                playniteApi.Dialogs.ShowErrorMessage(
                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncFailed"), backup.Name),
                    "Cloud Sync Error");
            }
        }

        /// <summary>
        /// 同步备份到云端，带后台同步选项
        /// </summary>
        private void SyncBackupWithBackgroundOption(SaveBackup backup)
        {
            bool success = false;
            bool useBackground = false;
            var cts = new System.Threading.CancellationTokenSource();
            var gameName = game.Name;

            // 启动后台上传任务
            var uploadTask = System.Threading.Tasks.Task.Run(async () =>
            {
                return await cloudSyncManager.UploadBackupToCloudAsync(backup, gameName);
            });

            // 显示进度窗口，带取消（后台同步）按钮
            playniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                progressArgs.IsIndeterminate = true;
                progressArgs.Text = string.Format(ResourceProvider.GetString("LOCSaveManagerMsgUploadingToCloud"), backup.Name);

                // 等待任务完成或用户取消
                while (!uploadTask.IsCompleted && !progressArgs.CancelToken.IsCancellationRequested)
                {
                    System.Threading.Thread.Sleep(100);
                }

                if (progressArgs.CancelToken.IsCancellationRequested)
                {
                    // 用户点击了"后台同步"按钮
                    useBackground = true;
                }
                else
                {
                    success = uploadTask.Result;
                }
            }, new Playnite.SDK.GlobalProgressOptions(
                ResourceProvider.GetString("LOCSaveManagerMsgSyncingToCloud"), true)
            {
                IsIndeterminate = true
            });

            if (useBackground)
            {
                // 后台继续上传，完成后通知
                uploadTask.ContinueWith(t =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (t.IsCompleted && !t.IsFaulted && t.Result)
                        {
                            playniteApi.Notifications.Add(new Playnite.SDK.NotificationMessage(
                                $"SaveManager_CloudSync_{backup.Name}_{DateTime.Now.Ticks}",
                                string.Format(ResourceProvider.GetString("LOCSaveManagerMsgBackupUploadComplete"), backup.Name),
                                Playnite.SDK.NotificationType.Info));
                        }
                        else
                        {
                            playniteApi.Notifications.Add(new Playnite.SDK.NotificationMessage(
                                $"SaveManager_CloudSync_Error_{backup.Name}",
                                string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncFailed"), backup.Name),
                                Playnite.SDK.NotificationType.Error));
                        }
                    });
                });
            }
            else if (!success)
            {
                playniteApi.Dialogs.ShowErrorMessage(
                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncFailed"), backup.Name),
                    "Cloud Sync Error");
            }
        }

        /// <summary>
        /// 前台同步删除多个备份
        /// </summary>
        private void SyncBackupsDeleteForeground(List<SaveBackup> backups)
        {
            int failedCount = 0;
            playniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                progressArgs.IsIndeterminate = false;
                progressArgs.ProgressMaxValue = backups.Count;

                for (int i = 0; i < backups.Count; i++)
                {
                    var backup = backups[i];
                    progressArgs.CurrentProgressValue = i;
                    progressArgs.Text = string.Format(ResourceProvider.GetString("LOCSaveManagerMsgDeletingFromCloud"), backup.Name);

                    var task = cloudSyncManager.DeleteBackupFromCloudAsync(backup);
                    task.Wait();
                    if (!task.Result) failedCount++;
                }
                progressArgs.CurrentProgressValue = backups.Count;
            }, new Playnite.SDK.GlobalProgressOptions(
                ResourceProvider.GetString("LOCSaveManagerMsgSyncingToCloud"), false)
            {
                IsIndeterminate = false
            });

            if (failedCount > 0)
            {
                playniteApi.Dialogs.ShowErrorMessage(
                    $"Failed to delete {failedCount} backup(s) from cloud.",
                    "Cloud Sync Error");
            }
        }

        /// <summary>
        /// 后台同步备份到云端
        /// </summary>
        private void SyncBackupToCloudBackground(SaveBackup backup, bool isUpload)
        {
            var gameName = game.Name;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    bool success = isUpload 
                        ? await cloudSyncManager.UploadBackupToCloudAsync(backup, gameName)
                        : await cloudSyncManager.DeleteBackupFromCloudAsync(backup);

                    // 在UI线程发送通知
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (success)
                        {
                            var message = isUpload
                                ? string.Format(ResourceProvider.GetString("LOCSaveManagerMsgBackupUploadComplete"), backup.Name)
                                : string.Format(ResourceProvider.GetString("LOCSaveManagerMsgBackupDeleteComplete"), backup.Name);
                            
                            // Playnite 通知
                            playniteApi.Notifications.Add(new Playnite.SDK.NotificationMessage(
                                $"SaveManager_CloudSync_{backup.Name}_{DateTime.Now.Ticks}",
                                message,
                                Playnite.SDK.NotificationType.Info));

                            // Windows 通知
                            ShowWindowsNotification(ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncComplete"), message);
                        }
                        else
                        {
                            var message = string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncFailed"), backup.Name);
                            
                            playniteApi.Notifications.Add(new Playnite.SDK.NotificationMessage(
                                $"SaveManager_CloudSync_Error_{backup.Name}",
                                message,
                                Playnite.SDK.NotificationType.Error));

                            ShowWindowsNotification("Save Manager", message);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        playniteApi.Notifications.Add(new Playnite.SDK.NotificationMessage(
                            $"SaveManager_CloudSync_Error_{backup.Name}",
                            $"Cloud sync error: {ex.Message}",
                            Playnite.SDK.NotificationType.Error));
                    });
                }
            });
        }

        /// <summary>
        /// 后台同步删除多个备份
        /// </summary>
        private void SyncBackupsDeleteBackground(List<SaveBackup> backups)
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                int successCount = 0;
                int failedCount = 0;

                foreach (var backup in backups)
                {
                    try
                    {
                        var success = await cloudSyncManager.DeleteBackupFromCloudAsync(backup);
                        if (success) successCount++;
                        else failedCount++;
                    }
                    catch
                    {
                        failedCount++;
                    }
                }

                // 在UI线程发送通知
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (failedCount == 0)
                    {
                        var message = $"Successfully deleted {successCount} backup(s) from cloud.";
                        
                        playniteApi.Notifications.Add(new Playnite.SDK.NotificationMessage(
                            $"SaveManager_CloudSync_DeleteBatch_{DateTime.Now.Ticks}",
                            message,
                            Playnite.SDK.NotificationType.Info));

                        ShowWindowsNotification(ResourceProvider.GetString("LOCSaveManagerMsgCloudSyncComplete"), message);
                    }
                    else
                    {
                        var message = $"Deleted {successCount} backup(s), failed {failedCount}.";
                        
                        playniteApi.Notifications.Add(new Playnite.SDK.NotificationMessage(
                            $"SaveManager_CloudSync_DeleteBatch_Error",
                            message,
                            Playnite.SDK.NotificationType.Error));

                        ShowWindowsNotification("Save Manager", message);
                    }
                });
            });
        }

        /// <summary>
        /// 显示 Windows 通知
        /// </summary>
        private void ShowWindowsNotification(string title, string message)
        {
            try
            {
                // 使用 Windows 10 Toast 通知
                var notifyIcon = new System.Windows.Forms.NotifyIcon();
                notifyIcon.Icon = System.Drawing.SystemIcons.Information;
                notifyIcon.Visible = true;
                notifyIcon.BalloonTipTitle = title;
                notifyIcon.BalloonTipText = message;
                notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
                notifyIcon.ShowBalloonTip(3000);

                // 延迟释放资源
                System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ =>
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                });
            }
            catch
            {
                // 忽略通知错误
            }
        }

        #endregion

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 存档路径项目（用于UI显示）
    /// </summary>
    public class SavePathItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _path;
        public string Path
        {
            get => _path;
            set 
            { 
                _path = value; 
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Path)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeIcon)));
            }
        }

        private bool _isDirectory;
        public bool IsDirectory
        {
            get => _isDirectory;
            set 
            { 
                _isDirectory = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirectory)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeIcon)));
            }
        }

        public string DisplayName => System.IO.Path.GetFileName(Path);
        public string TypeIcon => IsDirectory ? "📁" : "📄";
    }

    /// <summary>
    /// 简单的命令实现
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();
    }

    /// <summary>
    /// 带参数的命令实现
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (parameter == null && typeof(T).IsValueType)
                return _canExecute == null;
            return _canExecute?.Invoke((T)parameter) ?? true;
        }

        public void Execute(object parameter) => _execute((T)parameter);
    }
}
