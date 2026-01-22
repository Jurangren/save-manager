using System;
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
            set { _selectedBackup = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBackupSelected)); }
        }

        public bool IsBackupSelected => SelectedBackup != null;

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

        // 命令
        public ICommand AddFolderCommand { get; }
        public ICommand AddFileCommand { get; }
        public ICommand RemovePathCommand { get; }
        public ICommand CreateBackupCommand { get; }
        public ICommand RestoreBackupCommand { get; }
        public ICommand DeleteBackupCommand { get; }
        public ICommand OpenBackupFolderCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand EditBackupNoteCommand { get; }
        public ICommand ImportConfigCommand { get; }
        public ICommand ExportConfigCommand { get; }
        public ICommand ImportBackupCommand { get; }

        public SaveManagerViewModel(Game game, IPlayniteAPI playniteApi, BackupService backupService)
        {
            this.game = game;
            this.playniteApi = playniteApi;
            this.backupService = backupService;

            SavePaths = new ObservableCollection<SavePathItem>();
            Backups = new ObservableCollection<SaveBackup>();

            // 初始化命令
            AddFolderCommand = new RelayCommand(AddFolder);
            AddFileCommand = new RelayCommand(AddFile);
            RemovePathCommand = new RelayCommand<SavePathItem>(RemovePath);
            CreateBackupCommand = new RelayCommand(CreateBackup, () => HasSavePaths);
            RestoreBackupCommand = new RelayCommand(RestoreBackup, () => IsBackupSelected);
            DeleteBackupCommand = new RelayCommand(DeleteBackup, () => IsBackupSelected);
            OpenBackupFolderCommand = new RelayCommand(OpenBackupFolder);
            SaveConfigCommand = new RelayCommand(SaveConfig);
            EditBackupNoteCommand = new RelayCommand(EditBackupNote, () => IsBackupSelected);
            ImportConfigCommand = new RelayCommand(ImportConfig);
            ExportConfigCommand = new RelayCommand(ExportConfig);
            ImportBackupCommand = new RelayCommand(ImportBackup);

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
            }

            // 加载备份列表
            var backups = backupService.GetBackups(game.Id);
            foreach (var backup in backups)
            {
                Backups.Add(backup);
            }

            UpdateHasSavePaths();
        }

        private void AddFolder()
        {
            try
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "请选择存档文件夹";
                    dialog.ShowNewFolderButton = true;

                    // 尝试设置初始目录
                    // Fix: 规范化路径以提高兼容性
                    if (!string.IsNullOrEmpty(game.InstallDirectory))
                    {
                        var normalizedPath = game.InstallDirectory.Replace('/', '\\').TrimEnd('\\');
                        if (Directory.Exists(normalizedPath))
                        {
                            dialog.SelectedPath = normalizedPath;
                        }
                    }

                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        ProcessSelectedPath(dialog.SelectedPath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                playniteApi.Dialogs.ShowErrorMessage($"打开文件夹选择器失败: {ex.Message}", "错误");
            }
        }

        private void AddFile()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择要备份的文件",
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
                 playniteApi.Dialogs.ShowErrorMessage($"打开文件选择器失败: {ex.Message}", "错误");
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
                playniteApi.Dialogs.ShowMessage("该路径已添加。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 4. 添加
            SavePaths.Add(new SavePathItem
            {
                Path = finalPath,
                IsDirectory = isDirectory
            });
            UpdateHasSavePaths();
        }

        private void ImportConfig()
        {
            var path = playniteApi.Dialogs.SelectFile("配置文件 (*.json)|*.json");
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
                        $"从文件导导入了 {config.SavePaths.Count} 个路径。\n这将覆盖当前配置，是否继续？",
                        "确认导入",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        SavePaths.Clear();
                        foreach (var p in config.SavePaths)
                        {
                            SavePaths.Add(new SavePathItem { Path = p.Path, IsDirectory = p.IsDirectory });
                        }
                        UpdateHasSavePaths();
                        playniteApi.Dialogs.ShowMessage("配置导入成功。请记得点击保存。", "成功");
                    }
                }
                else
                {
                    playniteApi.Dialogs.ShowMessage("无效的配置文件格式。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                playniteApi.Dialogs.ShowErrorMessage($"导入失败: {ex.Message}", "错误");
            }
        }

        private void ExportConfig()
        {
            if (!HasSavePaths)
            {
                playniteApi.Dialogs.ShowMessage("当前没有配置任何路径，无法导出。", "提示");
                return;
            }

            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出存档路径配置",
                    Filter = "配置文件 (*.json)|*.json",
                    FileName = $"{game.Name}_SaveConfig.json"
                };

                // 获取主窗口作为父窗口
                var window = playniteApi.Dialogs.GetCurrentAppWindow();

                if (dialog.ShowDialog(window) == true)
                {
                    var config = new GameSaveConfig
                    {
                        GameId = game.Id,
                        GameName = game.Name,
                        SavePaths = new System.Collections.Generic.List<SavePath>()
                    };

                    foreach (var item in SavePaths)
                    {
                        config.SavePaths.Add(new SavePath
                        {
                            Path = item.Path,
                            IsDirectory = item.IsDirectory
                        });
                    }

                    var json = Playnite.SDK.Data.Serialization.ToJson(config, true);
                    File.WriteAllText(dialog.FileName, json);

                    playniteApi.Dialogs.ShowMessage($"配置已成功导出到:\n{dialog.FileName}", "成功");
                }
            }
            catch (Exception ex)
            {
                playniteApi.Dialogs.ShowErrorMessage($"导出配置失败: {ex.Message}", "错误");
            }
        }

        private void ImportBackup()
        {
            var path = playniteApi.Dialogs.SelectFile("备份文件 (*.zip)|*.zip");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var backup = backupService.ImportBackup(game.Id, game.Name, path);
                Backups.Insert(0, backup);
                playniteApi.Dialogs.ShowMessage($"备份导入成功！\n文件名: {backup.Name}", "成功");
            }
            catch (Exception ex)
            {
                 playniteApi.Dialogs.ShowErrorMessage($"导入备份失败: {ex.Message}", "错误");
            }
        }

        private void RemovePath(SavePathItem item)
        {
            if (item != null)
            {
                SavePaths.Remove(item);
                UpdateHasSavePaths();
            }
        }

        private void UpdateHasSavePaths()
        {
            HasSavePaths = SavePaths.Count > 0;
        }

        private void SaveConfig()
        {
            var config = new GameSaveConfig
            {
                GameId = game.Id,
                GameName = game.Name,
                SavePaths = SavePaths.Select(p => new SavePath
                {
                    Path = p.Path,
                    IsDirectory = p.IsDirectory
                }).ToList()
            };

            backupService.SaveGameConfig(config);
            playniteApi.Dialogs.ShowMessage("存档路径配置已保存。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CreateBackup()
        {
            if (!HasSavePaths)
            {
                playniteApi.Dialogs.ShowMessage("请先配置存档路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 先保存配置
            SaveConfig();

            try
            {
                var backup = backupService.CreateBackup(game.Id, game.Name, NewBackupDescription);
                Backups.Insert(0, backup);
                NewBackupDescription = string.Empty;
                playniteApi.Dialogs.ShowMessage($"备份创建成功！\n文件大小: {backup.FormattedSize}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                playniteApi.Dialogs.ShowErrorMessage($"创建备份失败: {ex.Message}", "错误");
            }
        }

        private void RestoreBackup()
        {
            if (SelectedBackup == null)
            {
                return;
            }

            var result = playniteApi.Dialogs.ShowMessage(
                $"确定要还原备份 \"{SelectedBackup.Name}\" 吗？\n\n这将覆盖当前的存档文件！\n\n建议先创建当前存档的备份。",
                "确认还原",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    backupService.RestoreBackup(SelectedBackup);
                    playniteApi.Dialogs.ShowMessage("备份还原成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"还原备份失败: {ex.Message}", "错误");
                }
            }
        }

        private void DeleteBackup()
        {
            if (SelectedBackup == null)
            {
                return;
            }

            var result = playniteApi.Dialogs.ShowMessage(
                $"确定要删除备份 \"{SelectedBackup.Name}\" 吗？\n\n此操作无法撤销！",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var backup = SelectedBackup;
                    backupService.DeleteBackup(backup);
                    Backups.Remove(backup);
                    SelectedBackup = null;
                    playniteApi.Dialogs.ShowMessage("备份已删除。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"删除备份失败: {ex.Message}", "错误");
                }
            }
        }

        private void EditBackupNote()
        {
            if (SelectedBackup == null)
            {
                return;
            }

            var result = playniteApi.Dialogs.SelectString(
                "请输入新的备注信息：",
                "修改备注",
                SelectedBackup.Description);

            if (result.Result)
            {
                try
                {
                    var backup = SelectedBackup;
                    var newDescription = result.SelectedString;
                    
                    backupService.UpdateBackupDescription(backup, newDescription);
                    
                    // 更新对象属性
                    backup.Description = newDescription;

                    // 强制刷新UI：通过替换集合中的项来触发更新
                    var index = Backups.IndexOf(backup);
                    if (index != -1)
                    {
                        Backups[index] = backup;
                        SelectedBackup = backup;
                    }
                    
                    playniteApi.Dialogs.ShowMessage("备注修改成功。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"修改备注失败: {ex.Message}", "错误");
                }
            }
        }

        private void OpenBackupFolder()
        {
            var backupsPath = backupService.GetGameBackupDirectory(game.Id, game.Name);
            
            if (!Directory.Exists(backupsPath))
            {
                playniteApi.Dialogs.ShowMessage("该游戏暂无备份文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            System.Diagnostics.Process.Start("explorer.exe", backupsPath);
        }

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
