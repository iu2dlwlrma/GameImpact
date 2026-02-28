#region

using System.Runtime.InteropServices;
using GameImpact.Abstractions.Input;
using GameImpact.Utilities.Logging;
using Rect = OpenCvSharp.Rect;

#endregion

namespace GameImpact.Core.Services
{
    /// <summary>调试用输入与 OCR 逻辑，不依赖 UI/Overlay。由 UI 根据返回值负责绘制。</summary>
    public interface IDebugActionsService
    {
        Task BringTargetToForegroundAsync();
        Task<bool> MouseClickAsync(int x, int y);
        Task MouseMoveAsync(int x, int y);
        Task KeyPressAsync(string key);
        Task KeyPressAsync(VirtualKey vk, IReadOnlyList<VirtualKey>? modifiers = null);

        /// <summary>OCR 识别区域。返回 (识别文本, ROI, 用于绘制的框列表)。</summary>
        (string? text, Rect roi, List<(Rect box, string text)> drawResults) TestOcr(int x, int y, int width, int height);

        /// <summary>查找文本。返回 (中心坐标, 用于绘制的单条结果)。</summary>
        ((int x, int y)? center, Rect fullRoi, (Rect box, string text)? matchForDraw) FindText(string searchText);

        /// <summary>全屏识别。返回 (中心+文本列表, 用于绘制的框列表)。</summary>
        (List<(int x, int y, string text)>? list, Rect fullRoi, List<(Rect box, string text)> drawResults) RecognizeFullScreen();
    }

    /// <summary>调试动作服务实现，依赖 GameContext。</summary>
    public sealed class DebugActionsService : IDebugActionsService
    {
        private readonly GameContext m_context;

        public DebugActionsService(GameContext context)
        {
            m_context = context;
        }

        public async Task BringTargetToForegroundAsync()
        {
            if (m_context.WindowHandle != nint.Zero)
            {
                NativeMethods.SetForegroundWindow(m_context.WindowHandle);
                await Task.Delay(300);
            }
        }

        public async Task<bool> MouseClickAsync(int x, int y)
        {
            await BringTargetToForegroundAsync();
            try
            {
                var result = m_context.Input.Mouse.ForegroundClickAt(x, y);
                Log.DebugScreen("[Input] 鼠标点击 ({X}, {Y})", x, y);
                return result;
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[Input] 点击失败");
                return false;
            }
        }

        public async Task MouseMoveAsync(int x, int y)
        {
            await BringTargetToForegroundAsync();
            try
            {
                m_context.Input.Mouse.MoveTo(x, y);
                Log.DebugScreen("[Input] 鼠标移动 ({X}, {Y})", x, y);
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[Input] 移动失败");
            }
        }

        public async Task KeyPressAsync(string key)
        {
            await BringTargetToForegroundAsync();
            try
            {
                if (Enum.TryParse<VirtualKey>(key, true, out var vk))
                {
                    m_context.Input.Keyboard.KeyPress(vk);
                }
                else
                {
                    m_context.Input.Keyboard.TextEntry(key);
                }
                Log.DebugScreen("[Input] 按键 {Key}", key);
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[Input] 按键失败");
            }
        }

        public async Task KeyPressAsync(VirtualKey vk, IReadOnlyList<VirtualKey>? modifiers = null)
        {
            await BringTargetToForegroundAsync();
            try
            {
                if (vk == VirtualKey.None)
                {
                    Log.WarnScreen("[Input] 无法识别按键");
                    return;
                }
                if (modifiers?.Count > 0)
                {
                    foreach (var mk in modifiers!)
                    {
                        m_context.Input.Keyboard.KeyDown(mk);
                    }
                    m_context.Input.Keyboard.KeyPress(vk);
                    foreach (var mk in modifiers!)
                    {
                        m_context.Input.Keyboard.KeyUp(mk);
                    }
                    Log.DebugScreen("[Input] 组合键 + {Key}", vk);
                }
                else
                {
                    m_context.Input.Keyboard.KeyPress(vk);
                    Log.DebugScreen("[Input] 按键 {Key}", vk);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[Input] 按键失败");
            }
        }

        public (string? text, Rect roi, List<(Rect box, string text)> drawResults) TestOcr(int x, int y, int width, int height)
        {
            var empty = new List<(Rect box, string text)>();
            if (m_context.Capture == null || !m_context.Capture.IsCapturing)
            {
                Log.WarnScreen("[OCR] 请先启动捕获");
                return (null, new Rect(x, y, width, height), empty);
            }
            try
            {
                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[OCR] 无法获取帧");
                    return (null, new Rect(x, y, width, height), empty);
                }
                using (frame)
                {
                    if (x < 0 || y < 0 || x + width > frame.Width || y + height > frame.Height)
                    {
                        Log.WarnScreen("[OCR] ROI 超出边界");
                        return (null, new Rect(x, y, width, height), empty);
                    }
                    var roi = new Rect(x, y, width, height);
                    var results = m_context.Ocr.Recognize(frame, roi);
                    var drawResults = results.Select(r => (r.BoundingBox, r.Text)).ToList();
                    if (results.Count == 0)
                    {
                        Log.InfoScreen("[OCR] 未识别到文字");
                        return (null, roi, drawResults);
                    }
                    var text = string.Join(" ", results.Select(r => r.Text));
                    var conf = results.Average(r => r.Confidence);
                    Log.InfoScreen("[OCR] 识别结果: {Text} (置信度: {Confidence})", text, conf.ToString("P0"));
                    return (text, roi, drawResults);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[OCR] 识别失败");
                return (null, new Rect(x, y, width, height), empty);
            }
        }

        public ((int x, int y)? center, Rect fullRoi, (Rect box, string text)? matchForDraw) FindText(string searchText)
        {
            if (m_context.Capture == null || !m_context.Capture.IsCapturing)
            {
                Log.WarnScreen("[OCR] 请先启动捕获");
                return (null, new Rect(), null);
            }
            try
            {
                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[OCR] 无法获取帧");
                    return (null, new Rect(), null);
                }
                using (frame)
                {
                    var results = m_context.Ocr.Recognize(frame);
                    var match = results.FirstOrDefault(r => r.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase));
                    var fullRoi = new Rect(0, 0, frame.Width, frame.Height);
                    if (match != null)
                    {
                        var centerX = match.BoundingBox.X + match.BoundingBox.Width / 2;
                        var centerY = match.BoundingBox.Y + match.BoundingBox.Height / 2;
                        Log.InfoScreen("[OCR] 找到 '{Text}' 在 ({X}, {Y})", searchText, centerX, centerY);
                        return ((centerX, centerY), fullRoi, (match.BoundingBox, match.Text));
                    }
                    Log.InfoScreen("[OCR] 未找到 '{Text}'，共识别 {Count} 个文本", searchText, results.Count);
                    return (null, fullRoi, null);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[OCR] 查找失败");
                return (null, new Rect(), null);
            }
        }

        public (List<(int x, int y, string text)>? list, Rect fullRoi, List<(Rect box, string text)> drawResults) RecognizeFullScreen()
        {
            var empty = new List<(Rect box, string text)>();
            if (m_context.Capture == null || !m_context.Capture.IsCapturing)
            {
                Log.WarnScreen("[OCR] 请先启动捕获");
                return (null, new Rect(), empty);
            }
            try
            {
                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[OCR] 无法获取帧");
                    return (null, new Rect(), empty);
                }
                using (frame)
                {
                    var results = m_context.Ocr.Recognize(frame);
                    var fullRoi = new Rect(0, 0, frame.Width, frame.Height);
                    var drawResults = results.Select(r => (r.BoundingBox, r.Text)).ToList();
                    var list = results.Select(r => (r.BoundingBox.X + r.BoundingBox.Width / 2, r.BoundingBox.Y + r.BoundingBox.Height / 2, r.Text)).ToList();
                    Log.InfoScreen("[OCR] 全屏识别完成，共 {Count} 个文本", results.Count);
                    return (list, fullRoi, drawResults);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[OCR] 全屏识别失败");
                return (null, new Rect(), empty);
            }
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(nint hWnd);
        }
    }
}
