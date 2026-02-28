#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GameImpact.UI.Models;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

#endregion

namespace GameImpact.UI.Views
{
    /// <summary>独立调试窗口，包含日志、预览、坐标/OCR/键盘测试等调试功能</summary>
    public partial class DebugWindow
    {
        private readonly MainModel m_model;
        private readonly List<string> m_templateFiles = new();
        private Key m_capturedKey = Key.None;
        private ModifierKeys m_capturedModifiers = ModifierKeys.None;
        private bool m_isCapturingKey;

        /// <summary>构造函数</summary>
        /// <param name="model">主视图模型</param>
        public DebugWindow(MainModel model)
        {
            InitializeComponent();
            m_model = model;
            DataContext = model;
            Loaded += (_, _) => RefreshTemplateList();
        }

        /// <summary>刷新模板列表</summary>
        private void RefreshTemplateList()
        {
            var previousSelected = TemplateListCombo.SelectedItem as string;
            m_templateFiles.Clear();
            m_templateFiles.AddRange(m_model.GetTemplateFileNames());
            TemplateListCombo.ItemsSource = null;
            TemplateListCombo.ItemsSource = m_templateFiles;

            if (!string.IsNullOrEmpty(previousSelected) && m_templateFiles.Contains(previousSelected))
            {
                TemplateListCombo.SelectedItem = previousSelected;
                return;
            }

            if (m_templateFiles.Count > 0)
            {
                TemplateListCombo.SelectedIndex = 0;
            }
            else
            {
                TemplateListCombo.SelectedIndex = -1;
            }
        }

        /// <summary>模板下拉框展开时刷新列表（支持外部删除/新增文件后立即同步）</summary>
        private void TemplateListCombo_DropDownOpened(object sender, EventArgs e)
        {
            RefreshTemplateList();
        }

        /// <summary>截图工具按钮点击事件处理</summary>
        private void ScreenshotTool_Click(object sender, RoutedEventArgs e)
        {
            m_model.StartScreenshotTool(RefreshTemplateList);
            AppendInputLog("请在目标窗口上拖拽框选区域，或按 ESC 取消");
        }

        /// <summary>模板匹配按钮点击事件处理</summary>
        private void MatchTemplate_Click(object sender, RoutedEventArgs e)
        {
            var selected = TemplateListCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected))
            {
                AppendInputLog("错误: 请先选择模板");
                return;
            }

            var (found, x, y, conf) = m_model.MatchWithTemplate(selected);
            if (found)
            {
                AppendInputLog($"模板匹配: '{selected}' 中心=({x},{y}) 置信度={conf:P0}");
                MouseX.Text = x.ToString();
                MouseY.Text = y.ToString();
            }
            else
            {
                AppendInputLog($"未匹配到模板: '{selected}'");
            }
        }

        /// <summary>打开模板文件夹按钮点击事件处理</summary>
        private void OpenTemplatesFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderPath = MainModel.TemplatesFolderPath;
                
                // 如果文件夹不存在，先创建
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // 打开文件夹
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true,
                    Verb = "open"
                });

                AppendInputLog($"已打开模板文件夹: {folderPath}");
            }
            catch (Exception ex)
            {
                AppendInputLog($"打开文件夹失败: {ex.Message}");
            }
        }

        /// <summary>设置模板ROI按钮点击事件处理</summary>
        private void SetTemplateRoi_Click(object sender, RoutedEventArgs e)
        {
            var selected = TemplateListCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected))
            {
                AppendInputLog("错误: 请先选择模板");
                return;
            }

            try
            {
                var templatePath = Path.Combine(MainModel.TemplatesFolderPath, selected);
                if (!File.Exists(templatePath))
                {
                    AppendInputLog($"错误: 模板文件不存在: {selected}");
                    return;
                }

                // 加载已保存的ROI设置
                var (matchRoi, textRoi) = LoadTemplateRoi(selected);

                // 显示ROI设置对话框
                var dialog = new TemplateRoiDialog(templatePath, matchRoi, textRoi)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    // 保存ROI设置
                    SaveTemplateRoi(selected, dialog.MatchRoi, dialog.TextRoi);
                    AppendInputLog($"已保存模板ROI设置: {selected}");
                    if (dialog.MatchRoi.HasValue)
                    {
                        var m = dialog.MatchRoi.Value;
                        AppendInputLog($"  匹配区域: ({m.X:F0}, {m.Y:F0}, {m.Width:F0}, {m.Height:F0})");
                    }
                    if (dialog.TextRoi.HasValue)
                    {
                        var t = dialog.TextRoi.Value;
                        AppendInputLog($"  文字区域: ({t.X:F0}, {t.Y:F0}, {t.Width:F0}, {t.Height:F0})");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendInputLog($"设置ROI失败: {ex.Message}");
            }
        }

        /// <summary>保存模板ROI设置到JSON文件</summary>
        private void SaveTemplateRoi(string templateFileName, OpenCvSharp.Rect? matchRoi, OpenCvSharp.Rect? textRoi)
        {
            try
            {
                var roiFilePath = GetRoiFilePath(templateFileName);
                var roiData = new
                {
                    MatchRoi = matchRoi.HasValue ? new
                    {
                        X = (int)matchRoi.Value.X,
                        Y = (int)matchRoi.Value.Y,
                        Width = (int)matchRoi.Value.Width,
                        Height = (int)matchRoi.Value.Height
                    } : null,
                    TextRoi = textRoi.HasValue ? new
                    {
                        X = (int)textRoi.Value.X,
                        Y = (int)textRoi.Value.Y,
                        Width = (int)textRoi.Value.Width,
                        Height = (int)textRoi.Value.Height
                    } : null
                };

                var json = JsonSerializer.Serialize(roiData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(roiFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DebugWindow] 保存ROI失败: {ex.Message}");
            }
        }

        /// <summary>从JSON文件加载模板ROI设置</summary>
        private (OpenCvSharp.Rect? matchRoi, OpenCvSharp.Rect? textRoi) LoadTemplateRoi(string templateFileName)
        {
            try
            {
                var roiFilePath = GetRoiFilePath(templateFileName);
                if (!File.Exists(roiFilePath))
                {
                    return (null, null);
                }

                var json = File.ReadAllText(roiFilePath);
                var roiData = JsonSerializer.Deserialize<JsonElement>(json);

                Rect? matchRoi = null;
                Rect? textRoi = null;

                if (roiData.TryGetProperty("MatchRoi", out var matchRoiElement) && matchRoiElement.ValueKind != JsonValueKind.Null)
                {
                    matchRoi = new OpenCvSharp.Rect(
                        matchRoiElement.GetProperty("X").GetInt32(),
                        matchRoiElement.GetProperty("Y").GetInt32(),
                        matchRoiElement.GetProperty("Width").GetInt32(),
                        matchRoiElement.GetProperty("Height").GetInt32()
                    );
                }

                if (roiData.TryGetProperty("TextRoi", out var textRoiElement) && textRoiElement.ValueKind != JsonValueKind.Null)
                {
                    textRoi = new OpenCvSharp.Rect(
                        textRoiElement.GetProperty("X").GetInt32(),
                        textRoiElement.GetProperty("Y").GetInt32(),
                        textRoiElement.GetProperty("Width").GetInt32(),
                        textRoiElement.GetProperty("Height").GetInt32()
                    );
                }

                return (matchRoi, textRoi);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DebugWindow] 加载ROI失败: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>获取ROI设置文件路径</summary>
        private string GetRoiFilePath(string templateFileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(templateFileName);
            return Path.Combine(MainModel.TemplatesFolderPath, $"{baseName}.roi.json");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>清除日志按钮点击事件处理</summary>
        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            m_model.ClearLog();
        }

        /// <summary>拾取坐标按钮点击事件处理</summary>
        private void PickCoord_Click(object sender, RoutedEventArgs e)
        {
            m_model.StartPickCoord((x, y) =>
            {
                Dispatcher.Invoke(() =>
                {
                    MouseX.Text = x.ToString();
                    MouseY.Text = y.ToString();
                    AppendInputLog($"拾取坐标: ({x}, {y})");
                });
            });
        }

        /// <summary>测试鼠标点击按钮点击事件处理</summary>
        private void TestMouseClick_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(MouseX.Text, out var x) && int.TryParse(MouseY.Text, out var y))
            {
                AppendInputLog($"鼠标点击: ({x}, {y})");
                m_model.TestMouseClick(x, y);
            }
            else
            {
                AppendInputLog("错误: 无效的坐标");
            }
        }

        /// <summary>测试鼠标移动按钮点击事件处理</summary>
        private void TestMouseMove_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(MouseX.Text, out var x) && int.TryParse(MouseY.Text, out var y))
            {
                AppendInputLog($"鼠标移动: ({x}, {y})");
                m_model.TestMouseMove(x, y);
            }
            else
            {
                AppendInputLog("错误: 无效的坐标");
            }
        }

        /// <summary>按键捕获区域鼠标按下事件处理</summary>
        private void KeyCapture_MouseDown(object sender, MouseButtonEventArgs e)
        {
            m_isCapturingKey = true;
            m_capturedKey = Key.None;
            m_capturedModifiers = ModifierKeys.None;
            KeyCaptureText.Text = "请按下按键...";
            KeyCaptureText.Foreground = (Brush)FindResource("AppAccent");
            KeyCaptureBorder.BorderBrush = (Brush)FindResource("AppAccent");
            KeyCaptureBorder.Focus();
            e.Handled = true;
        }

        /// <summary>按键捕获区域按键按下事件处理</summary>
        private void KeyCapture_KeyDown(object sender, KeyEventArgs e)
        {
            if (!m_isCapturingKey)
            {
                return;
            }

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // 如果只按了修饰键，先记录修饰键，等待实际按键
            if (key is Key.LeftCtrl or Key.RightCtrl or
                    Key.LeftShift or Key.RightShift or
                    Key.LeftAlt or Key.RightAlt or
                    Key.LWin or Key.RWin)
            {
                m_capturedModifiers = Keyboard.Modifiers;
                KeyCaptureText.Text = FormatKeyDisplay(ModifierKeys.None, key) + " + ...";
                e.Handled = true;
                return;
            }

            // 捕获到实际按键
            m_capturedModifiers = Keyboard.Modifiers;
            m_capturedKey = key;
            m_isCapturingKey = false;

            var displayText = FormatKeyDisplay(m_capturedModifiers, m_capturedKey);
            KeyCaptureText.Text = displayText;
            KeyCaptureText.Foreground = (Brush)FindResource("AppText");
            KeyCaptureBorder.BorderBrush = (Brush)FindResource("AppBorderSubtle");

            AppendInputLog($"已捕获按键: {displayText}");
            e.Handled = true;
        }

        /// <summary>按键捕获区域失去焦点事件处理</summary>
        private void KeyCapture_LostFocus(object sender, RoutedEventArgs e)
        {
            if (m_isCapturingKey)
            {
                m_isCapturingKey = false;
                if (m_capturedKey == Key.None)
                {
                    KeyCaptureText.Text = "点击捕获按键";
                    KeyCaptureText.Foreground = (Brush)FindResource("AppTextMuted");
                }
                KeyCaptureBorder.BorderBrush = (Brush)FindResource("AppBorderSubtle");
            }
        }

        private static string FormatKeyDisplay(ModifierKeys modifiers, Key key)
        {
            var parts = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                parts.Add("Ctrl");
            }
            if (modifiers.HasFlag(ModifierKeys.Alt))
            {
                parts.Add("Alt");
            }
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                parts.Add("Shift");
            }
            if (modifiers.HasFlag(ModifierKeys.Windows))
            {
                parts.Add("Win");
            }

            var keyName = key switch
            {
                    Key.Back => "Backspace",
                    Key.Return => "Enter",
                    Key.Capital => "CapsLock",
                    Key.Escape => "Esc",
                    Key.Prior => "PageUp",
                    Key.Next => "PageDown",
                    Key.Snapshot => "PrintScreen",
                    Key.Oem3 => "`",
                    Key.OemMinus => "-",
                    Key.OemPlus => "=",
                    Key.OemOpenBrackets => "[",
                    Key.Oem6 => "]",
                    Key.Oem5 => "\\",
                    Key.Oem1 => ";",
                    Key.OemQuotes => "'",
                    Key.OemComma => ",",
                    Key.OemPeriod => ".",
                    Key.OemQuestion => "/",
                    Key.LeftCtrl or Key.RightCtrl => "Ctrl",
                    Key.LeftShift or Key.RightShift => "Shift",
                    Key.LeftAlt or Key.RightAlt => "Alt",
                    Key.LWin or Key.RWin => "Win",
                    _ => key.ToString()
            };
            parts.Add(keyName);
            return string.Join(" + ", parts);
        }

        /// <summary>测试按键发送按钮点击事件处理</summary>
        private void TestKeyPress_Click(object sender, RoutedEventArgs e)
        {
            if (m_capturedKey == Key.None)
            {
                AppendInputLog("错误: 请先捕获按键");
                return;
            }

            var displayText = FormatKeyDisplay(m_capturedModifiers, m_capturedKey);
            AppendInputLog($"按键发送: {displayText}");
            m_model.TestKeyPress(m_capturedKey, m_capturedModifiers);
        }

        /// <summary>OCR查找按钮点击事件处理</summary>
        private void TestOcrFind_Click(object sender, RoutedEventArgs e)
        {
            var searchText = OcrSearchText.Text.Trim();
            if (string.IsNullOrEmpty(searchText))
            {
                AppendInputLog("错误: 请输入要查找的文本");
                return;
            }

            var result = m_model.FindText(searchText);
            if (result.HasValue)
            {
                MouseX.Text = result.Value.x.ToString();
                MouseY.Text = result.Value.y.ToString();
                AppendInputLog($"找到 '{searchText}' 坐标: ({result.Value.x}, {result.Value.y})");
            }
            else
            {
                AppendInputLog($"未找到 '{searchText}'");
            }
        }

        /// <summary>OCR全屏识别按钮点击事件处理</summary>
        private void TestOcrFull_Click(object sender, RoutedEventArgs e)
        {
            var results = m_model.RecognizeFullScreen();
            if (results != null && results.Count > 0)
            {
                AppendInputLog($"识别到 {results.Count} 个文本区域:");
                foreach (var r in results.Take(10))
                {
                    AppendInputLog($"  [{r.x},{r.y}] {r.text}");
                }
                if (results.Count > 10)
                {
                    AppendInputLog($"  ... 还有 {results.Count - 10} 个");
                }
            }
            else
            {
                AppendInputLog("未识别到文字");
            }
        }

        private void AppendInputLog(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            InputTestLog.Text = line + InputTestLog.Text;
            if (InputTestLog.Text.Length > 5000)
            {
                InputTestLog.Text = InputTestLog.Text[..5000];
            }
        }
    }
}
