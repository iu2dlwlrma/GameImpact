using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameImpact.Core.Services;

namespace GameImpact.UI.Services
{
    /// <summary>仅负责监听 Core 的 FrameReady，将帧复制到 WPF 位图并通知 UI 更新。</summary>
    public sealed class CapturePreviewController : IDisposable
    {
        private readonly ICapturePreviewProvider m_provider;
        private WriteableBitmap? m_writeableBitmap;

        public CapturePreviewController(ICapturePreviewProvider provider)
        {
            m_provider = provider;
        }

        public ImageSource? PreviewSource { get; private set; }
        public string PreviewFps { get; private set; } = "-";
        public string PreviewResolution { get; private set; } = "-";
        public string StatusText { get; private set; } = string.Empty;

        public event Action? PreviewUpdated;

        public void StartRendering()
        {
            m_provider.FrameReady += OnFrameReady;
            m_provider.Start();
        }

        public void StopRendering()
        {
            m_provider.Stop();
            m_provider.FrameReady -= OnFrameReady;
            PreviewSource = null;
            PreviewFps = "-";
            PreviewResolution = "-";
            StatusText = string.Empty;
            m_writeableBitmap = null;
        }

        private void OnFrameReady(object? sender, CaptureFrameEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var width = e.Width;
                var height = e.Height;
                var step = e.Stride;

                if (m_writeableBitmap == null || m_writeableBitmap.PixelWidth != width || m_writeableBitmap.PixelHeight != height)
                {
                    m_writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                    PreviewSource = m_writeableBitmap;
                }

                m_writeableBitmap.Lock();
                try
                {
                    var backBuffer = m_writeableBitmap.BackBuffer;
                    var backStride = m_writeableBitmap.BackBufferStride;
                    var copyBytes = width * 4;
                    unsafe
                    {
                        fixed (byte* src = e.Data)
                        {
                            var dst = (byte*)backBuffer;
                            if (backStride == step)
                            {
                                Buffer.MemoryCopy(src, dst, (long)height * copyBytes, (long)height * copyBytes);
                            }
                            else
                            {
                                for (var y = 0; y < height; y++)
                                {
                                    Buffer.MemoryCopy(src + y * step, dst + y * backStride, copyBytes, copyBytes);
                                }
                            }
                        }
                    }
                    m_writeableBitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
                }
                finally
                {
                    m_writeableBitmap.Unlock();
                }

                PreviewFps = e.Fps > 0 ? $"{e.Fps:F1}" : "-";
                PreviewResolution = e.ResolutionText;
                StatusText = e.Fps > 0 ? $"{e.Fps:F1} FPS" : string.Empty;
                PreviewUpdated?.Invoke();
            });
        }

        public void Dispose() => StopRendering();
    }
}
