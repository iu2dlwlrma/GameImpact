using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GameImpact.UI.Models;
using GameImpact.UI.Services;

namespace GameImpact.UI.Views;

/// <summary>
/// 窗口选择对话框
/// </summary>
public partial class WindowSelectDialog : Window
{
    private List<WindowInfo> m_allWindows = [];

    /// <summary>
    /// 选中的窗口
    /// </summary>
    public WindowInfo? SelectedWindow { get; private set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public WindowSelectDialog()
    {
        InitializeComponent();
        LoadWindows();
    }

    /// <summary>
    /// 标题栏鼠标左键按下事件处理
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>
    /// 加载所有窗口列表
    /// </summary>
    private void LoadWindows()
    {
        m_allWindows = WindowEnumerator.GetAllWindows()
            .OrderBy(w => w.ProcessName)
            .ThenBy(w => w.Title)
            .ToList();
        ApplyFilter();
    }

    /// <summary>
    /// 应用过滤条件
    /// </summary>
    private void ApplyFilter()
    {
        var filter = SearchBox.Text?.ToLower() ?? "";
        var filtered = string.IsNullOrEmpty(filter)
            ? m_allWindows
            : m_allWindows.Where(w =>
                w.ProcessName.ToLower().Contains(filter) ||
                w.Title.ToLower().Contains(filter)).ToList();

        WindowList.ItemsSource = filtered;
    }

    /// <summary>
    /// 搜索框文本变更事件处理
    /// </summary>
    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    /// <summary>
    /// 刷新按钮点击事件处理
    /// </summary>
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadWindows();
    }

    /// <summary>
    /// 确定按钮点击事件处理
    /// </summary>
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is WindowInfo window)
        {
            SelectedWindow = window;
            DialogResult = true;
            Close();
        }
    }

    /// <summary>
    /// 取消按钮点击事件处理
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 窗口列表双击事件处理
    /// </summary>
    private void WindowList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WindowList.SelectedItem is WindowInfo window)
        {
            SelectedWindow = window;
            DialogResult = true;
            Close();
        }
    }
}
