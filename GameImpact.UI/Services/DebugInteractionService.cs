using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GameImpact.Abstractions.Input;
using GameImpact.Core;
using GameImpact.Utilities.Logging;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

namespace GameImpact.UI.Services
{
    /// <summary>封装调试相关的输入模拟与 OCR 调用逻辑。</summary>
    public sealed class DebugInteractionService
    {
        private readonly GameContext m_context;
        private readonly IOverlayUiService m_overlay;

        public DebugInteractionService(GameContext context, IOverlayUiService overlay)
        {
            m_context = context;
            m_overlay = overlay;
        }

        /// <summary>将目标窗口切回前台。</summary>
        private async Task BringTargetToForegroundAsync()
        {
            if (m_context.WindowHandle != nint.Zero)
            {
                NativeMethods.SetForegroundWindow(m_context.WindowHandle);
                await Task.Delay(300);
            }
        }

        public async Task MouseClickAsync(int x, int y)
        {
            try
            {
                await BringTargetToForegroundAsync();

                var result = m_context.Input.Mouse.ForegroundClickAt(x, y);
                Log.DebugScreen("[Input] 鼠标点击 ({X}, {Y})", x, y);
                m_overlay.DrawClickMarker(x, y, result);
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[Input] 点击失败");
            }
        }

        public async Task MouseMoveAsync(int x, int y)
        {
            try
            {
                await BringTargetToForegroundAsync();

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
            try
            {
                await BringTargetToForegroundAsync();

                if (Enum.TryParse<VirtualKey>(key, true, out var vk))
                {
                    m_context.Input.Keyboard.KeyPress(vk);
                    Log.DebugScreen("[Input] 按键 {Key}", key);
                }
                else
                {
                    m_context.Input.Keyboard.TextEntry(key);
                    Log.DebugScreen("[Input] 文本输入 {Key}", key);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[Input] 按键失败");
            }
        }

        public async Task KeyPressAsync(Key wpfKey, ModifierKeys modifiers)
        {
            try
            {
                await BringTargetToForegroundAsync();

                var vk = WpfKeyToVirtualKey(wpfKey);
                if (vk == VirtualKey.None)
                {
                    Log.WarnScreen("[Input] 无法识别按键: {Key}", wpfKey);
                    return;
                }

                var modifierKeys = new List<VirtualKey>();
                if (modifiers.HasFlag(ModifierKeys.Control))
                {
                    modifierKeys.Add(VirtualKey.Control);
                }
                if (modifiers.HasFlag(ModifierKeys.Alt))
                {
                    modifierKeys.Add(VirtualKey.Menu);
                }
                if (modifiers.HasFlag(ModifierKeys.Shift))
                {
                    modifierKeys.Add(VirtualKey.Shift);
                }

                if (modifierKeys.Count > 0)
                {
                    foreach (var mk in modifierKeys)
                    {
                        m_context.Input.Keyboard.KeyDown(mk);
                    }

                    m_context.Input.Keyboard.KeyPress(vk);

                    foreach (var mk in modifierKeys)
                    {
                        m_context.Input.Keyboard.KeyUp(mk);
                    }

                    var modStr = string.Join("+", modifierKeys);
                    Log.DebugScreen("[Input] 组合键 {Mods}+{Key}", modStr, vk);
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

        private static VirtualKey WpfKeyToVirtualKey(Key wpfKey)
        {
            var vkCode = System.Windows.Input.KeyInterop.VirtualKeyFromKey(wpfKey);
            if (vkCode == 0)
            {
                return VirtualKey.None;
            }

            if (Enum.IsDefined(typeof(VirtualKey), (ushort)vkCode))
            {
                return (VirtualKey)(ushort)vkCode;
            }

            return VirtualKey.None;
        }

        public string? TestOcr(int x, int y, int width, int height)
        {
            if (m_context.Capture == null || !m_context.Capture.IsCapturing)
            {
                Log.WarnScreen("[OCR] 请先启动捕获");
                return null;
            }

            try
            {
                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[OCR] 无法获取帧");
                    return null;
                }

                using (frame)
                {
                    if (x < 0 || y < 0 || x + width > frame.Width || y + height > frame.Height)
                    {
                        Log.WarnScreen("[OCR] ROI 超出边界 (图像: {W}x{H})", frame.Width, frame.Height);
                        return null;
                    }

                    var roi = new Rect(x, y, width, height);
                    var results = m_context.Ocr.Recognize(frame, roi);

                    var drawResults = results.Select(r => (r.BoundingBox, r.Text)).ToList();
                    m_overlay.DrawOcrResult(roi, drawResults);

                    if (results.Count == 0)
                    {
                        Log.InfoScreen("[OCR] 未识别到文字");
                        return null;
                    }

                    var text = string.Join(" ", results.Select(r => r.Text));
                    var confidence = results.Average(r => r.Confidence);
                    Log.InfoScreen("[OCR] 识别结果: {Text} (置信度: {Confidence})", text, confidence.ToString("P0"));

                    return text;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[OCR] 识别失败");
                return null;
            }
        }

        public (int x, int y)? FindText(string searchText)
        {
            if (m_context.Capture == null || !m_context.Capture.IsCapturing)
            {
                Log.WarnScreen("[OCR] 请先启动捕获");
                return null;
            }

            try
            {
                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[OCR] 无法获取帧");
                    return null;
                }

                using (frame)
                {
                    var results = m_context.Ocr.Recognize(frame);
                    var match = results.FirstOrDefault(r =>
                            r.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        var fullRoi = new Rect(0, 0, frame.Width, frame.Height);
                        m_overlay.DrawOcrResult(fullRoi, [(match.BoundingBox, match.Text)]);

                        var centerX = match.BoundingBox.X + match.BoundingBox.Width / 2;
                        var centerY = match.BoundingBox.Y + match.BoundingBox.Height / 2;

                        Log.InfoScreen("[OCR] 找到 '{Text}' 在 ({X}, {Y})", searchText, centerX, centerY);
                        return (centerX, centerY);
                    }

                    Log.InfoScreen("[OCR] 未找到 '{Text}'，共识别 {Count} 个文本", searchText, results.Count);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[OCR] 查找失败");
                return null;
            }
        }

        public List<(int x, int y, string text)>? RecognizeFullScreen()
        {
            if (m_context.Capture == null || !m_context.Capture.IsCapturing)
            {
                Log.WarnScreen("[OCR] 请先启动捕获");
                return null;
            }

            try
            {
                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[OCR] 无法获取帧");
                    return null;
                }

                using (frame)
                {
                    var results = m_context.Ocr.Recognize(frame);

                    var fullRoi = new Rect(0, 0, frame.Width, frame.Height);
                    var drawResults = results.Select(r => (r.BoundingBox, r.Text)).ToList();
                    m_overlay.DrawOcrResult(fullRoi, drawResults);

                    Log.InfoScreen("[OCR] 全屏识别完成，共 {Count} 个文本", results.Count);

                    return results.Select(r => (
                            r.BoundingBox.X + r.BoundingBox.Width / 2,
                            r.BoundingBox.Y + r.BoundingBox.Height / 2,
                            r.Text
                    )).ToList();
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[OCR] 全屏识别失败");
                return null;
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(nint hWnd);
        }
    }
}

