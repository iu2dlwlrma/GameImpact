using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GameImpact.UI.Models;
using GameImpact.UI.Services;

namespace GameImpact.UI.Views;

public partial class WindowSelectDialog : Window
{
    private List<WindowInfo> _allWindows = [];

    public WindowInfo? SelectedWindow { get; private set; }

    public WindowSelectDialog()
    {
        InitializeComponent();
        LoadWindows();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void LoadWindows()
    {
        _allWindows = WindowEnumerator.GetAllWindows()
            .OrderBy(w => w.ProcessName)
            .ThenBy(w => w.Title)
            .ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = SearchBox.Text?.ToLower() ?? "";
        var filtered = string.IsNullOrEmpty(filter)
            ? _allWindows
            : _allWindows.Where(w =>
                w.ProcessName.ToLower().Contains(filter) ||
                w.Title.ToLower().Contains(filter)).ToList();

        WindowList.ItemsSource = filtered;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadWindows();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is WindowInfo window)
        {
            SelectedWindow = window;
            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

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
