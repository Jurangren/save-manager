using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SaveManager.Views
{
    /// <summary>
    /// SaveManagerView.xaml 的交互逻辑
    /// </summary>
    public partial class SaveManagerView : UserControl
    {
        public SaveManagerView()
        {
            InitializeComponent();
        }

        private void BackupsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ViewModels.SaveManagerViewModel viewModel && sender is ListBox listBox)
            {
                viewModel.UpdateSelection(listBox.SelectedItems);
            }
        }
    }

    /// <summary>
    /// 路径验证转换器（检测是否包含变量）
    /// </summary>
    public class PathValidationConverter : IValueConverter
    {
        public static readonly PathValidationConverter Instance = new PathValidationConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrEmpty(path))
                return false;

            // 检查是否包含支持的变量，或者是环境变量
            return path.Contains("{InstallDir}") ||
                   path.Contains("{EmulatorDir}") ||
                   path.Contains("{PlayniteDir}") ||
                   path.Contains("{GameDir}") ||
                   path.Contains("%") || // 环境变量，如 %USERPROFILE%
                   path.StartsWith("{"); // 其他可能的 Playnite 变量
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 字符串到可见性转换器
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public static readonly StringToVisibilityConverter Instance = new StringToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


