using System;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GameImpact.Core;
using GameImpact.Utilities.Logging;

namespace GameImpact.UI.Services
{
    /// <summary>负责基于 GameContext 提供预览图像、FPS、分辨率等信息的控制器。</summary>
    public sealed class CapturePreviewController : IDisposable
    {
        private readonly GameContext m_context;
        private readonly Stopwatch m_fpsTimer = new();
        private readonly DispatcherTimer m_logTimer;

        private bool m_isRendering;
        private int m_lastFrameCount;
        private WriteableBitmap? m_writeableBitmap;

        public CapturePreviewController(GameContext context)
        {
            m_context = context;

            // 使用独立的 DispatcherTimer 以便后续需要时扩展日志/状态刷新
            m_logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            m_logTimer.Tick += (_, _) => { };
            m_logTimer.Start();
        }

        public ImageSource? PreviewSource { get; private set; }

        public string PreviewFps { get; private set; } = "-";

        public string PreviewResolution { get; private set; } = "-";

        public string StatusText { get; private set; } = string.Empty;

        /// <summary>每次预览帧（或状态）更新后触发，用于通知外部同步属性。</summary>
        public event Action? PreviewUpdated;

        /// <summary>开始基于当前 Capture 渲染预览。</summary>
        public void StartRendering()
        {
            if (m_isRendering)
            {
                return;
            }

            if (m_context.Capture == null)
            {
                return;
            }

            m_lastFrameCount = 0;
            m_fpsTimer.Restart();
            m_isRendering = true;
            CompositionTarget.Rendering += OnRendering;
        }

        /// <summary>停止预览渲染并清理状态。</summary>
        public void StopRendering()
        {
            if (!m_isRendering)
            {
                return;
            }

            m_isRendering = false;
            CompositionTarget.Rendering -= OnRendering;

            PreviewSource = null;
            PreviewFps = "-";
            PreviewResolution = "-";
            StatusText = string.Empty;
            m_writeableBitmap = null;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (m_context.Capture?.IsCapturing != true || !m_isRendering)
            {
                return;
            }

            try
            {
                if (!m_context.Capture.TryGetFrameData(out var data, out var width, out var height, out var step))
                {
                    return;
                }

                try
                {
                    // 更新 FPS
                    if (m_fpsTimer.ElapsedMilliseconds >= 1000)
                    {
                        var currentFrameCount = m_context.Capture.FrameCount;
                        var framesDelta = currentFrameCount - m_lastFrameCount;
                        var fps = framesDelta * 1000.0 / m_fpsTimer.ElapsedMilliseconds;
                        StatusText = $"{fps:F1} FPS";
                        PreviewFps = $"{fps:F1}";
                        m_lastFrameCount = currentFrameCount;
                        m_fpsTimer.Restart();
                    }

                    PreviewResolution = $"{width} × {height}";

                    if (m_writeableBitmap == null ||
                        m_writeableBitmap.PixelWidth != width ||
                        m_writeableBitmap.PixelHeight != height)
                    {
                        m_writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        PreviewSource = m_writeableBitmap;
                    }

                    m_writeableBitmap.Lock();
                    try
                    {
                        var backBuffer = m_writeableBitmap.BackBuffer;
                        var backBufferStride = m_writeableBitmap.BackBufferStride;

                        unsafe
                        {
                            var copyBytes = width * 4;
                            var src = (byte*)data;
                            var dst = (byte*)backBuffer;

                            if (backBufferStride == step)
                            {
                                Buffer.MemoryCopy(src, dst, (long)height * copyBytes, (long)height * copyBytes);
                            }
                            else
                            {
                                for (var y = 0; y < height; y++)
                                {
                                    Buffer.MemoryCopy(src, dst, copyBytes, copyBytes);
                                    src += step;
                                    dst += backBufferStride;
                                }
                            }
                        }

                        m_writeableBitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
                    }
                    finally
                    {
                        m_writeableBitmap.Unlock();
                    }

                    PreviewUpdated?.Invoke();
                }
                finally
                {
                    m_context.Capture.ReleaseFrame();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("[Preview] Frame error: {Error}", ex.Message);
            }
        }

        public void Dispose()
        {
            StopRendering();
            m_logTimer.Stop();
        }
    }
}

