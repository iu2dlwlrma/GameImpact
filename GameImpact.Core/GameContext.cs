#region

using System.Threading.Tasks;
using GameImpact.Abstractions.Capture;
using GameImpact.Abstractions.Hotkey;
using GameImpact.Abstractions.Input;
using GameImpact.Abstractions.Recognition;
using GameImpact.Automation;
using GameImpact.Capture;
using GameImpact.Hotkey;
using GameImpact.Input;
using GameImpact.OCR;
using GameImpact.Vision;
using GameImpact.Utilities.Logging;

#endregion

namespace GameImpact.Core
{
    /// <summary>游戏上下文，管理屏幕捕获、输入模拟、OCR识别、热键等核心服务</summary>
    public class GameContext : IDisposable
    {
        private readonly Task<WindowsOcrEngine?> m_ocrTask;
        private WindowsOcrEngine? m_ocr;
        private readonly object m_ocrLock = new();

        /// <summary>构造函数</summary>
        public GameContext()
        {
            Log.Debug("[GameContext] Creating...");
            Input = InputFactory.CreateSendInput();
            Log.Debug("[GameContext] Input created");
            Hotkey = new HotkeyService();
            Log.Debug("[GameContext] Hotkey created");
            Recognition = new RecognitionService();
            Log.Debug("[GameContext] Recognition created");
            // OCR 引擎异步加载，避免阻塞启动
            // 注意：Windows OCR API 可能需要 STA 线程模型，如果后台创建失败，将延迟到首次使用时创建
            Log.Debug("[GameContext] Starting OCR engine async initialization...");
            m_ocrTask = Task.Run(() =>
            {
                try
                {
                    Log.Debug("[GameContext] Creating OCR engine in background thread...");
                    var engine = new WindowsOcrEngine();
                    Log.Debug("[GameContext] OCR engine created successfully");
                    return engine;
                }
                catch (Exception ex)
                {
                    Log.Warn("[GameContext] Failed to create OCR engine in background: {Message}. Will create on first use.", ex.Message);
                    return null; // 返回 null，表示需要延迟创建
                }
            });
            TaskEngine = new TaskEngine();
            Log.Debug("[GameContext] TaskEngine created");
            Log.Debug("[GameContext] Created");
        }
        /// <summary>目标窗口句柄</summary>
        public nint WindowHandle{ get; private set; }

        /// <summary>屏幕捕获服务</summary>
        public IScreenCapture Capture{ get; private set; } = null!;

        /// <summary>输入模拟服务</summary>
        public IInputSimulator Input{ get; }

        /// <summary>热键服务</summary>
        public IHotkeyService Hotkey{ get; }

        /// <summary>图像识别服务</summary>
        public IRecognitionService Recognition{ get; }

        /// <summary>OCR识别引擎（异步加载）</summary>
        public IOcrEngine Ocr
        {
            get
            {
                if (m_ocr == null)
                {
                    lock (m_ocrLock)
                    {
                        if (m_ocr == null)
                        {
                            // 等待异步任务完成
                            var result = m_ocrTask.GetAwaiter().GetResult();
                            if (result != null)
                            {
                                m_ocr = result;
                            }
                            else
                            {
                                // 如果后台创建失败，在当前线程（通常是 UI 线程，STA）中创建
                                Log.Debug("[GameContext] Creating OCR engine in current thread (fallback)...");
                                m_ocr = new WindowsOcrEngine();
                                Log.Debug("[GameContext] OCR engine created successfully (fallback)");
                            }
                        }
                    }
                }
                return m_ocr;
            }
        }

        /// <summary>自动化任务引擎</summary>
        public TaskEngine TaskEngine{ get; }

        /// <summary>是否已初始化</summary>
        public bool IsInitialized => WindowHandle != nint.Zero;

        /// <inheritdoc/>
        public void Dispose()
        {
            Log.Debug("[GameContext] Disposing");
            TaskEngine.Dispose();
            Capture?.Dispose();
            Hotkey.Dispose();
            // 等待 OCR 引擎加载完成后再释放
            try
            {
                lock (m_ocrLock)
                {
                    if (m_ocr != null)
                    {
                        m_ocr.Dispose();
                    }
                    else if (m_ocrTask.IsCompleted)
                    {
                        if (m_ocrTask.IsCompletedSuccessfully)
                        {
                            var result = m_ocrTask.Result;
                            if (result != null)
                            {
                                result.Dispose();
                            }
                        }
                    }
                    else
                    {
                        // 如果还在加载中，等待一小段时间
                        if (m_ocrTask.Wait(1000))
                        {
                            if (m_ocrTask.IsCompletedSuccessfully)
                            {
                                var result = m_ocrTask.Result;
                                if (result != null)
                                {
                                    result.Dispose();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[GameContext] Error disposing OCR engine: {Message}", ex.Message);
            }
            Log.Info("[GameContext] Disposed");
            GC.SuppressFinalize(this);
        }

        /// <summary>初始化游戏上下文，设置目标窗口并启动屏幕捕获</summary>
        /// <param name="windowHandle">目标窗口句柄</param>
        /// <param name="options">捕获选项</param>
        /// <param name="useGpuHdrConversion">是否使用GPU进行HDR转换</param>
        public void Initialize(nint windowHandle, CaptureOptions? options = null, bool useGpuHdrConversion = false)
        {
            if (windowHandle == nint.Zero)
            {
                Log.Error("[GameContext] Invalid window handle");
                throw new ArgumentException("Invalid window handle");
            }

            Log.Info("[GameContext] Initializing for window 0x{Handle:X}", windowHandle);

            // 先释放旧的 Capture
            Capture?.Dispose();

            WindowHandle = windowHandle;

            // 自动检测 HDR：如果用户没有明确指定，则自动检测窗口所在显示器的 HDR 状态
            var enableHdr = options?.EnableHdr ?? Direct3D11Helper.IsHdrEnabledForWindow(windowHandle);
            Log.Info("[GameContext] HDR mode: {HdrEnabled} (auto-detected: {AutoDetected})",
                    enableHdr, options?.EnableHdr == null);

            Capture = CaptureFactory.Create(enableHdr, useGpuHdrConversion);
            Capture.Start(windowHandle, options);

            // 设置后台输入目标窗口（用于 BackgroundClickAt 等 PostMessage 操作）
            Input.SetWindowHandle(windowHandle);

            TaskEngine.SetCapture(Capture);

            Log.Info("[GameContext] Initialized successfully");
        }
    }
}
