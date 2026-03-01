#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameImpact.Abstractions.Input;
using GameImpact.Core;
using GameImpact.Core.Services;
using GameImpact.Core.Windowing;
using GameImpact.UI.Events;
using GameImpact.UI.Services;
using GameImpact.UI.Views;
using GameImpact.Utilities.Logging;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

#endregion

namespace GameImpact.UI
{
    /// <summary>主视图模型，管理窗口选择、屏幕捕获、OCR识别等核心功能</summary>
    public partial class MainModel : ObservableObject
    {
        private readonly GameContext m_context;
        private readonly IDebugActionsService m_debugActions;
        private readonly Queue<string> m_logQueue = new();
        private readonly DispatcherTimer m_logTimer;
        private readonly IOverlayUiService m_overlay;
        private readonly CapturePreviewController m_previewController;
        private readonly IStatusTipsService m_statusTips;
        private readonly ITemplateMatchService m_templateMatch;
        private readonly ITemplateService m_templates;
        private readonly IWindowEnumerator m_windowEnumerator;
        [ObservableProperty] private bool _autoScrollLog = true;
        [ObservableProperty] private string _captureButtonText = "启动";
        [ObservableProperty] private bool _enablePreview = true;
        [ObservableProperty] private bool _isCapturing;
        [ObservableProperty] private bool _isIdle = true;
        [ObservableProperty] private bool _isPickingCoord;
        [ObservableProperty] private string _logText = "";
        [ObservableProperty] private string _previewFps = "-";
        [ObservableProperty] private string _previewResolution = "-";

        // Debug 面板属性
        [ObservableProperty] private ImageSource? _previewSource;
        [ObservableProperty] private string _statusMessage = "就绪";
        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private bool _useGpuHdrConversion = true;

        [ObservableProperty] private string _windowDisplayText = "未选择窗口";

        private nint m_hWnd;
        private string m_windowTitle = "";

        /// <summary>点击「启动」但当前未选择窗口时触发，宿主可尝试自动查找/启动游戏并调用 args.SetWindow 后继续启动。</summary>
        public event EventHandler<StartRequestedWhenNoWindowEventArgs>? StartRequestedWhenNoWindow;

        /// <summary>构造函数</summary>
        public MainModel(GameContext context,
                IWindowEnumerator windowEnumerator,
                ITemplateService templates,
                ITemplateMatchService templateMatch,
                IDebugActionsService debugActions,
                ICapturePreviewProvider previewProvider,
                IOverlayUiService overlay,
                IStatusTipsService statusTips)
        {
            m_context = context;
            m_windowEnumerator = windowEnumerator;
            m_templates = templates;
            m_templateMatch = templateMatch;
            m_debugActions = debugActions;
            m_overlay = overlay;
            m_statusTips = statusTips;
            m_previewController = new CapturePreviewController(previewProvider);
            m_previewController.PreviewUpdated += SyncPreviewFromController;

            m_logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            m_logTimer.Tick += OnLogTick;
            Log.OnScreenLogMessage += OnScreenLogReceived;
            m_logTimer.Start();
        }

        partial void OnStatusMessageChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            m_statusTips.Push(value);
            Log.Debug("[状态] {Message}", value);
        }

        /// <summary>模板文件夹路径（供 Debug 等 UI 使用）。</summary>
        public string TemplatesFolderPath => m_templates.TemplatesFolderPath;

        /// <summary>接收日志消息（仅DEBUG模式）</summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        private void OnLogReceived(string level, string message)
        {
#if DEBUG
            lock (m_logQueue)
            {
                m_logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
                while (m_logQueue.Count > 300)
                {
                    m_logQueue.Dequeue();
                }
            }
#endif
        }

        /// <summary>接收屏幕日志消息</summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        private void OnScreenLogReceived(string level, string message)
        {
            lock (m_logQueue)
            {
                m_logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
                while (m_logQueue.Count > 300)
                {
                    m_logQueue.Dequeue();
                }
            }
        }

        /// <summary>日志定时器回调：更新日志文本显示</summary>
        private void OnLogTick(object? sender, EventArgs e)
        {
            lock (m_logQueue)
            {
                if (m_logQueue.Count > 0)
                {
                    LogText = string.Join("\n", m_logQueue);
                }
            }
        }

        /// <summary>预览开关变更时的处理</summary>
        /// <param name="value">是否启用预览</param>
        partial void OnEnablePreviewChanged(bool value)
        {
            if (!value)
            {
                m_previewController.StopRendering();
                SyncPreviewFromController();
            }
        }
        private void SyncPreviewFromController()
        {
            PreviewSource = m_previewController.PreviewSource;
            PreviewFps = m_previewController.PreviewFps;
            PreviewResolution = m_previewController.PreviewResolution;
            StatusText = m_previewController.StatusText;
        }


#region 窗口相关

        public bool SetProcess(IWindowEnumerator enumerator, string processName, string processTitle)
        {
            try
            {
                var win = WindowFinder.FindByProcessNameAndProcessTitle(enumerator, processName, processTitle);
                if (win != null)
                {
                    SetSelectedWindow(win.Handle, win.Title, win.ProcessName);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[{AppName} - {AppTitle}] 自动查找进程: {Ex}", processName, processTitle, ex.Message);
            }
            return false;
        }
        [RelayCommand]
        private void SelectWindow()
        {
            var dialog = new WindowSelectDialog(m_windowEnumerator);

            // 设置 Owner 为主窗口，使对话框居中显示
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                dialog.Owner = mainWindow;
            }

            if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
            {
                ApplySelectedWindow(dialog.SelectedWindow.Handle, dialog.SelectedWindow.Title, dialog.SelectedWindow.ProcessName);
                Log.InfoScreen("[UI] 选择窗口: {Process} ({Handle})", dialog.SelectedWindow.ProcessName, dialog.SelectedWindow.HandleText);
            }
        }

        /// <summary>由宿主或自动查找逻辑调用，将指定窗口设为当前选择（不启动捕获）。</summary>
        public void SetSelectedWindow(nint hWnd, string title, string processName)
        {
            ApplySelectedWindow(hWnd, title, processName);
        }

        private void ApplySelectedWindow(nint hWnd, string title, string processName)
        {
            m_hWnd = hWnd;
            m_windowTitle = title;
            WindowDisplayText = string.IsNullOrEmpty(title) ? processName : title;
            StartCapture();
        }

        [RelayCommand]
        private void ToggleCapture()
        {
            if (IsCapturing)
            {
                StopCapture();
            }
            else
            {
                if (m_hWnd == nint.Zero)
                {
                    var args = new StartRequestedWhenNoWindowEventArgs(ApplySelectedWindow);
                    StartRequestedWhenNoWindow?.Invoke(this, args);
                    if (m_hWnd == nint.Zero)
                    {
                        return;
                    }
                }
                StartCapture();
            }
        }

        /// <summary>开始捕获（宿主在异步设置窗口后可调用以自动开始）。</summary>
        public void StartCapture()
        {
            if (m_hWnd == nint.Zero)
            {
                StatusMessage = "请先选择窗口";
                return;
            }

            try
            {
                m_context.Initialize(m_hWnd, useGpuHdrConversion: UseGpuHdrConversion);

                IsCapturing = true;
                IsIdle = false;
                CaptureButtonText = "停止";
                Log.InfoScreen("[UI] 开始捕获 (GPU HDR: {UseGpu})", UseGpuHdrConversion ? "开启" : "关闭");

                // 启动 Overlay 窗口
                m_overlay.AttachTo(m_hWnd);

                m_previewController.StartRendering();
                SyncPreviewFromController();
            }
            catch (Exception ex)
            {
                StatusMessage = $"启动失败: {ex.Message}";
                Log.ErrorScreen(ex, "[UI] 启动捕获失败");
            }
        }

        private void StopCapture()
        {
            m_previewController.StopRendering();
            m_context.Capture?.Stop();

            // 关闭 Overlay
            m_overlay.Detach();

            IsCapturing = false;
            IsIdle = true;
            CaptureButtonText = "启动";
            StatusText = "";
            StatusMessage = "已停止";
            PreviewSource = null;
            PreviewFps = "-";
            PreviewResolution = "-";
            Log.InfoScreen("[UI] 停止捕获");
        }

#endregion

        public void ClearLog()
        {
            lock (m_logQueue)
            {
                m_logQueue.Clear();
                LogText = "";
            }
        }

        public void StartPickCoord(Action<int, int> onPicked)
        {
            if (!IsCapturing)
            {
                Log.WarnScreen("[拾取] 请先启动捕获");
                return;
            }

            IsPickingCoord = true;
            Log.InfoScreen("[拾取] 开始拾取坐标，点击目标位置或按 ESC 取消");

            m_overlay.StartPickCoord((x, y) =>
            {
                IsPickingCoord = false;
                Log.InfoScreen("[拾取] 坐标: ({X}, {Y})", x, y);
                onPicked(x, y);
            });
        }

        public void ShowOverlayInfo(string key, string text)
        {
            m_overlay.ShowInfo(key, text);
        }

        public void HideOverlayInfo(string key)
        {
            m_overlay.HideInfo(key);
        }

        public async void TestMouseClick(int x, int y)
        {
            var success = await m_debugActions.MouseClickAsync(x, y);
            m_overlay.DrawClickMarker(x, y, success);
        }

        public async void TestMouseMove(int x, int y)
        {
            await m_debugActions.MouseMoveAsync(x, y);
        }

        public async void TestKeyPress(string key)
        {
            await m_debugActions.KeyPressAsync(key);
        }

        /// <summary>发送按键（支持组合键），从 WPF Key + ModifierKeys 转换为 VirtualKey</summary>
        public async void TestKeyPress(Key wpfKey, ModifierKeys modifiers)
        {
            var vk = WpfKeyToVirtualKey(wpfKey);
            var modList = new List<VirtualKey>();
            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                modList.Add(VirtualKey.Control);
            }
            if (modifiers.HasFlag(ModifierKeys.Alt))
            {
                modList.Add(VirtualKey.Menu);
            }
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                modList.Add(VirtualKey.Shift);
            }
            await m_debugActions.KeyPressAsync(vk, modList.Count > 0 ? modList : null);
        }

        private static VirtualKey WpfKeyToVirtualKey(Key wpfKey)
        {
            var vkCode = KeyInterop.VirtualKeyFromKey(wpfKey);
            if (vkCode == 0)
            {
                return VirtualKey.None;
            }
            return Enum.IsDefined(typeof(VirtualKey), (ushort)vkCode) ? (VirtualKey)(ushort)vkCode : VirtualKey.None;
        }

        public string? TestOcr(int x, int y, int width, int height)
        {
            var (text, roi, drawResults) = m_debugActions.TestOcr(x, y, width, height);
            if (drawResults.Count > 0)
            {
                m_overlay.DrawOcrResult(roi, drawResults);
            }
            return text;
        }

        /// <summary>在整个窗口中查找指定文本，返回中心坐标</summary>
        public (int x, int y)? FindText(string searchText)
        {
            var (center, fullRoi, matchForDraw) = m_debugActions.FindText(searchText);
            if (matchForDraw.HasValue)
            {
                m_overlay.DrawOcrResult(fullRoi, [(matchForDraw.Value.box, matchForDraw.Value.text)]);
            }
            return center;
        }

        /// <summary>识别整个窗口的文字</summary>
        public List<(int x, int y, string text)>? RecognizeFullScreen()
        {
            var (list, fullRoi, drawResults) = m_debugActions.RecognizeFullScreen();
            if (drawResults.Count > 0)
            {
                m_overlay.DrawOcrResult(fullRoi, drawResults);
            }
            return list;
        }

        /// <summary>启动截图工具：在目标窗口上框选区域，截取后弹出命名对话框保存到模板文件夹。</summary>
        public void StartScreenshotTool(Action? onSaved = null)
        {
            if (!IsCapturing)
            {
                Log.WarnScreen("[截图] 请先启动捕获");
                return;
            }

            m_overlay.StartScreenshotRegion((x, y, w, h) =>
            {
                if (w <= 0 || h <= 0)
                {
                    Log.InfoScreen("[截图] 已取消");
                    return;
                }
                try
                {
                    var crop = m_templateMatch.CaptureRegion(x, y, w, h);
                    if (crop == null)
                    {
                        return;
                    }
                    try
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ShowScreenshotNameDialog(crop, onSaved);
                        });
                    }
                    finally
                    {
                        crop.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorScreen(ex, "[截图] 保存失败");
                }
            });
        }

        /// <summary>显示截图命名对话框</summary>
        private void ShowScreenshotNameDialog(Mat screenshot, Action? onSaved)
        {
            try
            {
                var defaultFileName = $"template_{DateTime.Now:yyyyMMdd_HHmmss}";
                var dialog = new ScreenshotNameDialog(screenshot, defaultFileName);

                // 设置对话框的所有者窗口
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    dialog.Owner = mainWindow;
                }

                if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SavedFilePath))
                {
                    try
                    {
                        m_templates.SaveTemplate(screenshot, dialog.SavedFilePath);
                        Log.InfoScreen("[截图] 已保存: {Name}", dialog.SavedFilePath);
                        onSaved?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.ErrorScreen(ex, "[截图] 保存失败");
                    }
                }
                else
                {
                    Log.InfoScreen("[截图] 已取消");
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[截图] 对话框显示失败");
            }
        }

        /// <summary>获取模板文件夹中的模板文件名列表（按时间倒序）。</summary>
        public List<string> GetTemplateFileNames()
        {
            return m_templates.GetTemplateFileNames();
        }

        public string GetTemplatePath(string fileName)
        {
            return m_templates.GetTemplatePath(fileName);
        }
        public (Rect? matchRoi, Rect? textRoi) LoadTemplateRoi(string templateFileName)
        {
            return m_templates.LoadTemplateRoi(templateFileName);
        }
        public void SaveTemplateRoi(string templateFileName, Rect? matchRoi, Rect? textRoi)
        {
            m_templates.SaveTemplateRoi(templateFileName, matchRoi, textRoi);
        }

        /// <summary>用当前画面与指定模板匹配，返回是否找到及中心坐标、置信度。</summary>
        public (bool found, int centerX, int centerY, double confidence) MatchWithTemplate(string? fileName)
        {
            var r = m_templateMatch.MatchWithTemplateAndText(fileName);
            if (r.Found)
            {
                m_overlay.DrawOcrResult(r.MatchDraw.roi, r.MatchDraw.draw);
                if (r.TextDraw.HasValue)
                {
                    m_overlay.DrawOcrResult(r.TextDraw.Value.roi, r.TextDraw.Value.draw);
                }
            }
            return (r.Found, r.CenterX, r.CenterY, r.Confidence);
        }

        /// <summary>用当前画面与指定模板匹配，并提取文字区域进行OCR识别。返回是否找到、中心坐标、置信度和识别的文字。</summary>
        public (bool found, int centerX, int centerY, double confidence, string? text) MatchWithTemplateAndText(string? fileName, Rect? textRegion = null)
        {
            var r = m_templateMatch.MatchWithTemplateAndText(fileName, textRegion);
            if (r.Found)
            {
                m_overlay.DrawOcrResult(r.MatchDraw.roi, r.MatchDraw.draw);
                if (r.TextDraw.HasValue)
                {
                    m_overlay.DrawOcrResult(r.TextDraw.Value.roi, r.TextDraw.Value.draw);
                }
            }
            return (r.Found, r.CenterX, r.CenterY, r.Confidence, r.Text);
        }

        public void Cleanup()
        {
            m_overlay.ForceClose();
        }
    }
}
