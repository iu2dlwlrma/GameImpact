#region

using System.Diagnostics;
using System.Runtime.InteropServices;

#endregion

namespace GameImpact.Core.Services
{
    /// <summary>预览帧数据事件参数。</summary>
    public sealed class CaptureFrameEventArgs : EventArgs
    {
        public CaptureFrameEventArgs(byte[] data, int width, int height, int stride, double fps, string resolutionText)
        {
            Data = data;
            Width = width;
            Height = height;
            Stride = stride;
            Fps = fps;
            ResolutionText = resolutionText;
        }
        public byte[] Data{ get; }
        public int Width{ get; }
        public int Height{ get; }
        public int Stride{ get; }
        public double Fps{ get; }
        public string ResolutionText{ get; }
    }

    /// <summary>从 Capture 拉帧并发出 FrameReady 事件，供 UI 监听并绘制。与 WPF 无关。</summary>
    public interface ICapturePreviewProvider
    {
        event EventHandler<CaptureFrameEventArgs>? FrameReady;
        void Start();
        void Stop();
    }

    /// <summary>基于 GameContext.Capture 的预览提供器，定时拉帧并触发事件。</summary>
    public sealed class CapturePreviewProvider : ICapturePreviewProvider
    {
        private readonly GameContext m_context;
        private readonly Stopwatch m_fpsTimer = new();
        private readonly object m_sync = new();
        private double m_lastFps;
        private int m_lastFrameCount;
        private bool m_running;
        private Timer? m_timer;

        public CapturePreviewProvider(GameContext context)
        {
            m_context = context;
        }

        public event EventHandler<CaptureFrameEventArgs>? FrameReady;

        public void Start()
        {
            lock (m_sync)
            {
                if (m_running)
                {
                    return;
                }
                m_running = true;
                m_lastFrameCount = 0;
                m_fpsTimer.Restart();
                m_timer = new Timer(OnTick, null, 0, 33); // ~30fps
            }
        }

        public void Stop()
        {
            lock (m_sync)
            {
                if (!m_running)
                {
                    return;
                }
                m_running = false;
                m_timer?.Dispose();
                m_timer = null;
            }
        }

        private void OnTick(object? _)
        {
            if (!m_running || m_context.Capture?.IsCapturing != true)
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
                    var copyLen = height * step;
                    var copy = new byte[copyLen];
                    Marshal.Copy(data, copy, 0, copyLen);
                    if (m_fpsTimer.ElapsedMilliseconds >= 1000)
                    {
                        var cur = m_context.Capture.FrameCount;
                        m_lastFps = (cur - m_lastFrameCount) * 1000.0 / m_fpsTimer.ElapsedMilliseconds;
                        m_lastFrameCount = cur;
                        m_fpsTimer.Restart();
                    }
                    var resolutionText = $"{width} × {height}";
                    var args = new CaptureFrameEventArgs(copy, width, height, step, m_lastFps, resolutionText);
                    FrameReady?.Invoke(this, args);
                }
                finally
                {
                    m_context.Capture.ReleaseFrame();
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
