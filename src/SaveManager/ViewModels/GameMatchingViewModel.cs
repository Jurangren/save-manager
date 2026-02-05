using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Models;
using SaveManager.Models;
using SaveManager.Services;

namespace SaveManager.ViewModels
{
    /// <summary>
    /// 游戏匹配项 - 表示一个配置和其匹配状态
    /// </summary>
    public class GameMatchingItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 关联的游戏配置
        /// </summary>
        public GameSaveConfig Config { get; set; }

        /// <summary>
        /// 配置中的游戏名称
        /// </summary>
        public string ConfigGameName => Config?.GameName ?? "Unknown";

        /// <summary>
        /// 存档路径数量
        /// </summary>
        public int SavePathCount => Config?.SavePaths?.Count ?? 0;

        /// <summary>
        /// 备份数量
        /// </summary>
        public int BackupCount { get; set; }

        private Game _matchedGame;
        /// <summary>
        /// 当前设备上匹配到的游戏
        /// </summary>
        public Game MatchedGame
        {
            get => _matchedGame;
            set
            {
                // 如果正在更新可选游戏列表且试图设置为 null，忽略这次变化
                // 这是为了防止 ComboBox 在刷新 ItemsSource 时误清除选中项
                if (_isUpdatingAvailableGames && value == null && _matchedGame != null)
                {
                    return;
                }

                if (_matchedGame != value)
                {
                    _matchedGame = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MatchedGameName));
                    OnPropertyChanged(nameof(IsMatched));
                    OnPropertyChanged(nameof(MatchStatusText));
                    OnPropertyChanged(nameof(MatchStatusColor));
                    OnPropertyChanged(nameof(MatchStatusSortValue));
                }
            }
        }

        /// <summary>
        /// 匹配到的游戏名称
        /// </summary>
        public string MatchedGameName => MatchedGame?.Name ?? "";

        /// <summary>
        /// 是否已匹配
        /// </summary>
        public bool IsMatched => MatchedGame != null;

        /// <summary>
        /// 匹配状态文本
        /// </summary>
        public string MatchStatusText => IsMatched ? "✓ 已关联" : "✗ 未关联";

        /// <summary>
        /// 匹配状态颜色
        /// </summary>
        public string MatchStatusColor => IsMatched ? "#4CAF50" : "#F44336";

        /// <summary>
        /// 匹配状态排序值（0=已匹配，1=未匹配）
        /// </summary>
        public int MatchStatusSortValue => IsMatched ? 0 : 1;

        // 标志：正在更新可选游戏列表，防止触发 MatchedGame 变化
        private bool _isUpdatingAvailableGames = false;

        /// <summary>
        /// 可选择的游戏列表（用于下拉选择）
        /// </summary>
        public ObservableCollection<Game> AvailableGames { get; set; } = new ObservableCollection<Game>();

        /// <summary>
        /// 游戏搜索文本
        /// </summary>
        private string _gameSearchText = "";
        public string GameSearchText
        {
            get => _gameSearchText;
            set
            {
                if (_gameSearchText != value)
                {
                    _gameSearchText = value;
                    OnPropertyChanged();
                    FilterAvailableGames();
                }
            }
        }

        // 所有游戏（未筛选）
        private List<Game> _allAvailableGames = new List<Game>();

        public void SetAllAvailableGames(IEnumerable<Game> games)
        {
            _allAvailableGames = games.ToList();
            FilterAvailableGames();
        }

        private void FilterAvailableGames()
        {
            _isUpdatingAvailableGames = true;
            try
            {
                // 保存当前匹配的游戏
                var currentMatch = _matchedGame;

                // 获取筛选结果
                var query = _allAvailableGames.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(_gameSearchText))
                {
                    query = query.Where(g => g.Name.IndexOf(_gameSearchText, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                var filteredGames = query.OrderBy(g => g.Name).Take(100).ToList();

                // 增量更新：移除不在新列表中的项目
                var toRemove = AvailableGames.Where(g => g != currentMatch && !filteredGames.Contains(g)).ToList();
                foreach (var game in toRemove)
                {
                    AvailableGames.Remove(game);
                }

                // 增量更新：添加新列表中存在但当前列表没有的项目
                foreach (var game in filteredGames)
                {
                    if (!AvailableGames.Contains(game))
                    {
                        // 找到正确的插入位置（跳过当前匹配项）
                        int insertIndex = 0;
                        if (currentMatch != null && AvailableGames.Count > 0 && AvailableGames[0] == currentMatch)
                        {
                            insertIndex = 1;
                        }
                        for (int i = insertIndex; i < AvailableGames.Count; i++)
                        {
                            if (string.Compare(AvailableGames[i].Name, game.Name, StringComparison.OrdinalIgnoreCase) > 0)
                            {
                                insertIndex = i;
                                break;
                            }
                            insertIndex = i + 1;
                        }
                        AvailableGames.Insert(insertIndex, game);
                    }
                }

                // 确保当前匹配的游戏始终在列表第一位
                if (currentMatch != null)
                {
                    if (!AvailableGames.Contains(currentMatch))
                    {
                        AvailableGames.Insert(0, currentMatch);
                    }
                    else if (AvailableGames.IndexOf(currentMatch) != 0)
                    {
                        AvailableGames.Remove(currentMatch);
                        AvailableGames.Insert(0, currentMatch);
                    }
                }
            }
            finally
            {
                _isUpdatingAvailableGames = false;
            }
        }
    }

    /// <summary>
    /// 排序方向枚举
    /// </summary>
    public enum SortDirection
    {
        None,
        Ascending,
        Descending
    }

    /// <summary>
    /// 游戏匹配视图模型
    /// </summary>
    public class GameMatchingViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly IPlayniteAPI playniteApi;
        private readonly BackupService backupService;
        private readonly bool isFullMode;
        private readonly CloudSyncManager cloudSyncManager;
        private readonly Func<bool> getCloudSyncEnabled;

        /// <summary>
        /// 所有游戏匹配项（源数据）
        /// </summary>
        private List<GameMatchingItem> allMatchingItems = new List<GameMatchingItem>();

        /// <summary>
        /// 筛选后的匹配项（绑定到UI）
        /// </summary>
        public ObservableCollection<GameMatchingItem> MatchingItems { get; set; } = new ObservableCollection<GameMatchingItem>();

        /// <summary>
        /// 是否为完整模式（显示所有配置，包括已匹配的）
        /// </summary>
        public bool IsFullMode => isFullMode;

        /// <summary>
        /// 标题
        /// </summary>
        public string Title => isFullMode 
            ? ResourceProvider.GetString("LOCSaveManagerGameMatchingTitleFull")
            : ResourceProvider.GetString("LOCSaveManagerGameMatchingTitleSimple");

        /// <summary>
        /// 说明文字
        /// </summary>
        public string Description => isFullMode
            ? ResourceProvider.GetString("LOCSaveManagerGameMatchingDescFull")
            : ResourceProvider.GetString("LOCSaveManagerGameMatchingDescSimple");

        private string _searchText = "";
        /// <summary>
        /// 搜索文本
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    ApplyFilterAndSort();
                }
            }
        }

        private SortDirection _configNameSortDirection = SortDirection.None;
        /// <summary>
        /// 配置名排序方向
        /// </summary>
        public SortDirection ConfigNameSortDirection
        {
            get => _configNameSortDirection;
            set
            {
                if (_configNameSortDirection != value)
                {
                    _configNameSortDirection = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConfigNameSortIcon));
                    ApplyFilterAndSort();
                }
            }
        }

        /// <summary>
        /// 配置名排序图标
        /// </summary>
        public string ConfigNameSortIcon => GetSortIcon(_configNameSortDirection);

        private SortDirection _statusSortDirection = SortDirection.None;
        /// <summary>
        /// 状态排序方向
        /// </summary>
        public SortDirection StatusSortDirection
        {
            get => _statusSortDirection;
            set
            {
                if (_statusSortDirection != value)
                {
                    _statusSortDirection = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusSortIcon));
                    ApplyFilterAndSort();
                }
            }
        }

        /// <summary>
        /// 状态排序图标
        /// </summary>
        public string StatusSortIcon => GetSortIcon(_statusSortDirection);

        private string GetSortIcon(SortDirection direction)
        {
            switch (direction)
            {
                case SortDirection.Ascending: return "▲";
                case SortDirection.Descending: return "▼";
                default: return "⇅";
            }
        }

        private int _unmatchedCount;
        /// <summary>
        /// 未匹配数量
        /// </summary>
        public int UnmatchedCount
        {
            get => _unmatchedCount;
            set
            {
                if (_unmatchedCount != value)
                {
                    _unmatchedCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        /// <summary>
        /// 状态文本
        /// </summary>
        public string StatusText => string.Format(
            ResourceProvider.GetString("LOCSaveManagerGameMatchingStatus"),
            MatchingItems.Count,
            UnmatchedCount);

        // Playnite 所有游戏列表（用于匹配选择）
        private List<Game> allGames;

        // 已经匹配的 GameId 集合（用于缩减模式排除）
        private HashSet<Guid> matchedGameIds = new HashSet<Guid>();

        // 命令
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AutoMatchCommand { get; }
        public ICommand ClearMatchCommand { get; }
        public ICommand ToggleConfigSortCommand { get; }
        public ICommand ToggleStatusSortCommand { get; }
        public ICommand DeleteConfigCommand { get; }

        /// <summary>
        /// 窗口关闭事件
        /// </summary>
        public event Action<bool> RequestClose;

        /// <summary>
        /// 窗口关闭回调
        /// </summary>
        public Action CloseAction { get; set; }

        /// <summary>
        /// 过滤的配置ID列表（仅显示这些配置）
        /// </summary>
        private readonly List<Guid> filterConfigIds = null;

        public GameMatchingViewModel(IPlayniteAPI playniteApi, BackupService backupService, bool fullMode = false, CloudSyncManager cloudSyncManager = null, Func<bool> getCloudSyncEnabled = null)
        {
            this.playniteApi = playniteApi;
            this.backupService = backupService;
            this.isFullMode = fullMode;
            this.cloudSyncManager = cloudSyncManager;
            this.getCloudSyncEnabled = getCloudSyncEnabled;

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(Cancel);
            AutoMatchCommand = new RelayCommand(AutoMatchAll);
            ClearMatchCommand = new RelayCommand<GameMatchingItem>(ClearMatch);
            ToggleConfigSortCommand = new RelayCommand(ToggleConfigSort);
            ToggleStatusSortCommand = new RelayCommand(ToggleStatusSort);
            DeleteConfigCommand = new RelayCommand<GameMatchingItem>(DeleteConfig);

            LoadData();
        }

        /// <summary>
        /// 新配置模式的构造函数（仅显示指定的配置ID列表）
        /// </summary>
        public GameMatchingViewModel(IPlayniteAPI playniteApi, BackupService backupService, List<Guid> configIdsToShow, CloudSyncManager cloudSyncManager = null, Func<bool> getCloudSyncEnabled = null)
        {
            this.playniteApi = playniteApi;
            this.backupService = backupService;
            this.isFullMode = true; // 显示所有，包括已匹配的
            this.filterConfigIds = configIdsToShow;
            this.cloudSyncManager = cloudSyncManager;
            this.getCloudSyncEnabled = getCloudSyncEnabled;

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(Cancel);
            AutoMatchCommand = new RelayCommand(AutoMatchAll);
            ClearMatchCommand = new RelayCommand<GameMatchingItem>(ClearMatch);
            ToggleConfigSortCommand = new RelayCommand(ToggleConfigSort);
            ToggleStatusSortCommand = new RelayCommand(ToggleStatusSort);
            DeleteConfigCommand = new RelayCommand<GameMatchingItem>(DeleteConfig);

            LoadDataForNewConfigs();
        }

        private void ToggleConfigSort()
        {
            // 重置状态排序
            _statusSortDirection = SortDirection.None;
            OnPropertyChanged(nameof(StatusSortDirection));
            OnPropertyChanged(nameof(StatusSortIcon));

            // 循环切换
            ConfigNameSortDirection = (SortDirection)(((int)ConfigNameSortDirection + 1) % 3);
        }

        private void ToggleStatusSort()
        {
            // 重置配置名排序
            _configNameSortDirection = SortDirection.None;
            OnPropertyChanged(nameof(ConfigNameSortDirection));
            OnPropertyChanged(nameof(ConfigNameSortIcon));

            // 循环切换
            StatusSortDirection = (SortDirection)(((int)StatusSortDirection + 1) % 3);
        }

        private void LoadData()
        {
            // 获取 Playnite 所有游戏
            allGames = playniteApi.Database.Games.ToList();

            // 获取所有游戏配置
            var configs = backupService.GetAllGameConfigs();

            foreach (var config in configs)
            {
                var item = new GameMatchingItem
                {
                    Config = config,
                    BackupCount = backupService.GetBackupsByConfigId(config.ConfigId).Count
                };

                // 尝试自动匹配
                TryAutoMatch(item);

                // 缩减模式下只显示未匹配的
                if (!isFullMode && item.IsMatched)
                {
                    // 记录已匹配的 GameId
                    if (item.MatchedGame != null)
                    {
                        matchedGameIds.Add(item.MatchedGame.Id);
                    }
                    continue;
                }

                // 设置可选游戏列表
                UpdateAvailableGames(item);

                allMatchingItems.Add(item);
            }

            ApplyFilterAndSort();
        }

        /// <summary>
        /// 加载新配置数据（仅显示指定的配置ID）
        /// </summary>
        private void LoadDataForNewConfigs()
        {
            // 获取 Playnite 所有游戏
            allGames = playniteApi.Database.Games.ToList();

            // 获取所有游戏配置，但只处理指定的配置ID
            var configs = backupService.GetAllGameConfigs()
                .Where(c => filterConfigIds.Contains(c.ConfigId))
                .ToList();

            foreach (var config in configs)
            {
                var item = new GameMatchingItem
                {
                    Config = config,
                    BackupCount = backupService.GetBackupsByConfigId(config.ConfigId).Count
                };

                // 尝试自动匹配
                TryAutoMatch(item);

                // 记录已匹配的 GameId
                if (item.IsMatched && item.MatchedGame != null)
                {
                    matchedGameIds.Add(item.MatchedGame.Id);
                }

                // 设置可选游戏列表
                UpdateAvailableGames(item);

                allMatchingItems.Add(item);
            }

            // 默认按状态倒序排序（未匹配在上面）
            _statusSortDirection = SortDirection.Descending;
            OnPropertyChanged(nameof(StatusSortDirection));
            OnPropertyChanged(nameof(StatusSortIcon));

            ApplyFilterAndSort();
        }

        /// <summary>
        /// 应用筛选和排序
        /// </summary>
        private void ApplyFilterAndSort()
        {
            IEnumerable<GameMatchingItem> query = allMatchingItems;

            // 应用搜索筛选
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                query = query.Where(i => 
                    i.ConfigGameName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (i.MatchedGameName != null && i.MatchedGameName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            // 应用排序
            if (_configNameSortDirection == SortDirection.Ascending)
            {
                query = query.OrderBy(i => i.ConfigGameName);
            }
            else if (_configNameSortDirection == SortDirection.Descending)
            {
                query = query.OrderByDescending(i => i.ConfigGameName);
            }
            else if (_statusSortDirection == SortDirection.Ascending)
            {
                query = query.OrderBy(i => i.MatchStatusSortValue).ThenBy(i => i.ConfigGameName);
            }
            else if (_statusSortDirection == SortDirection.Descending)
            {
                query = query.OrderByDescending(i => i.MatchStatusSortValue).ThenBy(i => i.ConfigGameName);
            }

            // 更新显示列表
            MatchingItems.Clear();
            foreach (var item in query)
            {
                MatchingItems.Add(item);
            }

            UpdateUnmatchedCount();
        }

        /// <summary>
        /// 尝试自动匹配游戏
        /// </summary>
        /// <summary>
        /// 尝试自动匹配游戏
        /// </summary>
        /// <summary>
        /// 尝试自动匹配游戏
        /// </summary>
        private void TryAutoMatch(GameMatchingItem item, bool force = false)
        {
            // 1. 先尝试通过 GameIds 列表精确匹配
            // 如果是强制匹配（用户手动点击），则跳过此步，直接进行名称匹配
            // 避免用户清除匹配后，点击自动匹配又立即被已保存的 GameId 拉回去
            if (!force)
            {
                foreach (var gameId in item.Config.GameIds)
                {
                    var game = allGames.FirstOrDefault(g => g.Id == gameId);
                    // 即使是 ID 匹配，最好也检查一下是否被占用了（虽然原则上 ID 是一对一的）
                    // 但这里我们假设 ID 匹配是绝对可信的（可能是用户之前保存的）
                    if (game != null)
                    {
                        // 确保匹配的游戏在可选列表中
                        if (!item.AvailableGames.Contains(game))
                        {
                            item.AvailableGames.Insert(0, game);
                        }
                        item.MatchedGame = game;
                        return;
                    }
                }
            }

            // 如果用户明确禁用了自动匹配，且不是强制匹配模式，则不进行名称匹配
            if (item.Config.DisableAutoMatch && !force)
            {
                return;
            }

            // 2. 尝试通过游戏名称匹配（排除被用户明确排除的游戏，且排除已被其他配置匹配的游戏）
            if (!string.IsNullOrEmpty(item.Config.GameName))
            {
                // 先过滤出所有潜在的候选者（未被排除(除非强制) 且 未被占用）
                // 注意：在 AutoMatchAll 循环中，matchedGameIds 会动态更新，所以这里能感知到刚才匹配的游戏
                var candidates = allGames.Where(g => 
                    (force || !item.Config.IsGameIdExcluded(g.Id)) && 
                    !matchedGameIds.Contains(g.Id));

                // 精确匹配
                var exactMatches = candidates.Where(g => 
                    g.Name.Equals(item.Config.GameName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // 如果只有一个精确匹配，直接使用
                if (exactMatches.Count == 1)
                {
                    var match = exactMatches[0];
                    // 确保匹配的游戏在可选列表中
                    if (!item.AvailableGames.Contains(match))
                    {
                        item.AvailableGames.Insert(0, match);
                    }
                    item.MatchedGame = match;
                    if (force) item.Config.DisableAutoMatch = false;
                    return;
                }
                
                // 如果有多个精确匹配，视为有歧义，不自动匹配
                if (exactMatches.Count > 1)
                {
                    return; 
                }

                // 模糊匹配（包含）
                var fuzzyMatches = candidates.Where(g =>
                    g.Name.IndexOf(item.Config.GameName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Config.GameName.IndexOf(g.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                // 如果只有一个模糊匹配结果，自动匹配
                if (fuzzyMatches.Count == 1)
                {
                    var match = fuzzyMatches[0];
                    // 确保匹配的游戏在可选列表中
                    if (!item.AvailableGames.Contains(match))
                    {
                        item.AvailableGames.Insert(0, match);
                    }
                    item.MatchedGame = match;
                    if (force) item.Config.DisableAutoMatch = false;
                }
            }
        }

        /// <summary>
        /// 更新可选游戏列表
        /// </summary>
        private void UpdateAvailableGames(GameMatchingItem item)
        {
            var availableGames = new List<Game>();

            foreach (var game in allGames.OrderBy(g => g.Name))
            {
                // 完整模式显示所有游戏
                // 缩减模式排除已被其他配置匹配的游戏
                if (isFullMode || !matchedGameIds.Contains(game.Id) || game == item.MatchedGame)
                {
                    availableGames.Add(game);
                }
            }

            item.SetAllAvailableGames(availableGames);
        }

        /// <summary>
        /// 自动匹配所有未匹配的项
        /// </summary>
        private void AutoMatchAll()
        {
            var newlyMatchedGames = new List<Game>();
            
            foreach (var item in allMatchingItems.Where(i => !i.IsMatched).ToList())
            {
                // 手动触发时使用强制匹配，忽略 DisableAutoMatch 标志
                TryAutoMatch(item, force: true);
                if (item.IsMatched)
                {
                    matchedGameIds.Add(item.MatchedGame.Id);
                    newlyMatchedGames.Add(item.MatchedGame);
                }
            }

            // 从其他未匹配项目的可选列表中移除新匹配的游戏（增量更新）
            foreach (var game in newlyMatchedGames)
            {
                foreach (var item in allMatchingItems.Where(i => !i.IsMatched))
                {
                    item.AvailableGames.Remove(game);
                }
            }

            ApplyFilterAndSort();
        }

        /// <summary>
        /// 清除匹配
        /// </summary>
        private void ClearMatch(GameMatchingItem item)
        {
            if (item?.MatchedGame != null)
            {
                var clearedGame = item.MatchedGame;
                matchedGameIds.Remove(clearedGame.Id);
                
                // 直接设置为 null
                item.MatchedGame = null;
                
                // 将被释放的游戏添加到其他未匹配项目的可选列表中（增量更新，不清空重建）
                foreach (var i in allMatchingItems.Where(x => x != item && !x.IsMatched))
                {
                    if (!i.AvailableGames.Contains(clearedGame))
                    {
                        int insertIndex = FindInsertIndex(i.AvailableGames, clearedGame.Name);
                        i.AvailableGames.Insert(insertIndex, clearedGame);
                    }
                }
                
                UpdateUnmatchedCount();
            }
        }

        private void DeleteConfig(GameMatchingItem item)
        {
            if (item == null) return;

            var result = playniteApi.Dialogs.ShowMessage(
                string.Format(ResourceProvider.GetString("LOCSaveManagerGameMatchingDeleteConfigConfirm"), item.ConfigGameName),
                "Save Manager",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 删除本地数据
                    backupService.DeleteGameConfig(item.Config.ConfigId);

                    // 如果启用了云同步，删除云端文件夹
                    if (cloudSyncManager != null && (getCloudSyncEnabled?.Invoke() ?? false))
                    {
                        DeleteCloudFolderWithProgress(item.ConfigGameName);
                    }

                    // 如果该项占用了游戏，需要释放该游戏 ID
                    if (item.IsMatched && item.MatchedGame != null)
                    {
                        var releasedGame = item.MatchedGame;
                        matchedGameIds.Remove(releasedGame.Id);
                        
                        // 将被释放的游戏放回其他项的可选列表中
                        foreach (var i in allMatchingItems.Where(x => x != item && !x.IsMatched))
                        {
                            if (!i.AvailableGames.Contains(releasedGame))
                            {
                                int insertIndex = FindInsertIndex(i.AvailableGames, releasedGame.Name);
                                i.AvailableGames.Insert(insertIndex, releasedGame);
                            }
                        }
                    }

                    // 从列表中移除 (使用 ID 匹配以确保移除)
                    var itemsToRemove = allMatchingItems.Where(i => i.Config.ConfigId == item.Config.ConfigId).ToList();
                    foreach (var i in itemsToRemove)
                    {
                        allMatchingItems.Remove(i);
                        MatchingItems.Remove(i);
                    }

                    // 更新计数
                    UpdateUnmatchedCount();

                    OnPropertyChanged(nameof(StatusText));
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage(ex.Message, "Delete Failed");
                }
            }
        }

        /// <summary>
        /// 删除云端文件夹（带进度显示和重试功能）
        /// </summary>
        private void DeleteCloudFolderWithProgress(string gameName)
        {
            bool shouldRetry = true;

            while (shouldRetry)
            {
                shouldRetry = false;
                bool success = false;
                Exception syncException = null;
                int totalFiles = 0;

                // 第1步：获取云端文件数量
                playniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    progressArgs.IsIndeterminate = true;
                    progressArgs.Text = ResourceProvider.GetString("LOCSaveManagerMsgCheckingCloudFiles");

                    try
                    {
                        var task = cloudSyncManager.GetCloudGameFilesAsync(gameName);
                        task.Wait();
                        totalFiles = task.Result?.Count ?? 0;
                    }
                    catch (Exception ex)
                    {
                        syncException = ex;
                    }
                }, new GlobalProgressOptions(
                    ResourceProvider.GetString("LOCSaveManagerMsgDeletingFromCloud"), false)
                {
                    IsIndeterminate = true
                });

                if (syncException != null)
                {
                    // 显示重试/忽略对话框
                    if (!ShowRetryIgnoreDialog(syncException.Message))
                    {
                        return; // 用户选择忽略
                    }
                    shouldRetry = true;
                    continue;
                }

                if (totalFiles == 0)
                {
                    // 云端没有文件，直接返回
                    return;
                }

                // 第2步：删除云端文件夹
                playniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    progressArgs.IsIndeterminate = false;
                    progressArgs.ProgressMaxValue = totalFiles;
                    progressArgs.CurrentProgressValue = 0;
                    progressArgs.Text = string.Format(ResourceProvider.GetString("LOCSaveManagerMsgDeletingCloudFolder"), gameName, 0, totalFiles);

                    try
                    {
                        var task = cloudSyncManager.DeleteCloudGameFolderAsync(gameName);
                        
                        // 模拟进度（因为 purge 是一次性删除）
                        int simulatedProgress = 0;
                        while (!task.IsCompleted)
                        {
                            System.Threading.Thread.Sleep(100);
                            if (simulatedProgress < totalFiles - 1)
                            {
                                simulatedProgress++;
                                progressArgs.CurrentProgressValue = simulatedProgress;
                                progressArgs.Text = string.Format(
                                    ResourceProvider.GetString("LOCSaveManagerMsgDeletingCloudFolder"), 
                                    gameName, simulatedProgress, totalFiles);
                            }
                        }

                        success = task.Result;
                        if (success)
                        {
                            progressArgs.CurrentProgressValue = totalFiles;
                        }
                    }
                    catch (Exception ex)
                    {
                        syncException = ex;
                        success = false;
                    }
                }, new GlobalProgressOptions(
                    string.Format(ResourceProvider.GetString("LOCSaveManagerMsgDeletingCloudFolder"), gameName, 0, totalFiles), false)
                {
                    IsIndeterminate = false
                });

                if (!success)
                {
                    string errorMsg = syncException?.Message ?? 
                        string.Format(ResourceProvider.GetString("LOCSaveManagerMsgCloudDeleteFailed"), gameName);
                    
                    if (ShowRetryIgnoreDialog(errorMsg))
                    {
                        shouldRetry = true;
                    }
                    // 用户选择忽略，退出循环
                }
            }
        }

        /// <summary>
        /// 显示重试/忽略对话框
        /// </summary>
        private bool ShowRetryIgnoreDialog(string errorMsg)
        {
            var options = new List<MessageBoxOption>
            {
                new MessageBoxOption(
                    ResourceProvider.GetString("LOCSaveManagerBtnRetry"), true, false),
                new MessageBoxOption(
                    ResourceProvider.GetString("LOCSaveManagerBtnIgnore"), false, true)
            };

            var selectedOption = playniteApi.Dialogs.ShowMessage(
                errorMsg,
                "Cloud Sync Error",
                MessageBoxImage.Error,
                options);

            return selectedOption == options[0]; // 返回true表示重试
        }

        /// <summary>
        /// 当用户选择了新的匹配游戏时调用
        /// </summary>
        public void OnMatchedGameChanged(GameMatchingItem item, Game oldGame, Game newGame)
        {
            // 从已匹配集合中移除旧游戏
            if (oldGame != null)
            {
                matchedGameIds.Remove(oldGame.Id);
                
                // 将旧游戏添加回其他未匹配项目的可选列表（增量更新）
                foreach (var i in allMatchingItems.Where(x => x != item && !x.IsMatched))
                {
                    if (!i.AvailableGames.Contains(oldGame))
                    {
                        // 按名称排序插入
                        int insertIndex = FindInsertIndex(i.AvailableGames, oldGame.Name);
                        i.AvailableGames.Insert(insertIndex, oldGame);
                    }
                }
            }

            // 添加新游戏到已匹配集合
            if (newGame != null)
            {
                matchedGameIds.Add(newGame.Id);
                
                // 从其他未匹配项目的可选列表中移除新游戏（增量更新）
                foreach (var i in allMatchingItems.Where(x => x != item))
                {
                    i.AvailableGames.Remove(newGame);
                }
            }

            UpdateUnmatchedCount();
        }

        /// <summary>
        /// 找到按名称排序的插入位置
        /// </summary>
        private int FindInsertIndex(ObservableCollection<Game> games, string name)
        {
            for (int j = 0; j < games.Count; j++)
            {
                if (string.Compare(games[j].Name, name, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    return j;
                }
            }
            return games.Count;
        }

        private void UpdateUnmatchedCount()
        {
            UnmatchedCount = MatchingItems.Count(i => !i.IsMatched);
            OnPropertyChanged(nameof(StatusText));
        }

        /// <summary>
        /// 保存匹配结果
        /// </summary>
        private void Save()
        {
            int updatedCount = 0;

            foreach (var item in allMatchingItems)
            {
                // 获取当前设备上，配置中原本关联的 GameId（如果有）
                var existingGameId = item.Config.GameIds.FirstOrDefault(id => 
                    allGames.Any(g => g.Id == id));

                if (item.MatchedGame != null)
                {
                    // 有新匹配 - 启用自动匹配
                    item.Config.DisableAutoMatch = false;

                    if (existingGameId != Guid.Empty && existingGameId != item.MatchedGame.Id)
                    {
                        // 移除旧的匹配
                        item.Config.RemoveGameId(existingGameId);
                    }

                    // 确保新匹配的游戏不在排除列表中
                    item.Config.UnexcludeGameId(item.MatchedGame.Id);

                    // 添加新的 GameId
                    if (item.Config.AddGameId(item.MatchedGame.Id))
                    {
                        updatedCount++;
                    }
                    else if (existingGameId != Guid.Empty && existingGameId != item.MatchedGame.Id)
                    {
                        // 即使 AddGameId 返回 false（已存在），但移除了旧的，也算更新
                        updatedCount++;
                    }
                }
                else
                {
                    // 清除匹配 - 禁用自动匹配，防止下次自动匹配
                    item.Config.DisableAutoMatch = true;

                    if (existingGameId != Guid.Empty)
                    {
                        item.Config.RemoveGameId(existingGameId);
                        item.Config.ExcludeGameId(existingGameId);
                        updatedCount++;
                    }
                }
            }

            // 保存更新后的配置
            backupService.SaveAllConfigs();

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
                        // 记录日志但不阻止保存
                        System.Diagnostics.Debug.WriteLine($"Failed to upload config after game matching save: {ex.Message}");
                    }
                });
            }

            if (updatedCount > 0)
            {
                playniteApi.Dialogs.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCSaveManagerGameMatchingSaveSuccess"), updatedCount),
                    "Save Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            RequestClose?.Invoke(true);
        }

        private void Cancel()
        {
            RequestClose?.Invoke(false);
        }
    }
}
