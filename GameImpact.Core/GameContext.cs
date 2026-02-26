using GameImpact.Abstractions.Capture;
using GameImpact.Abstractions.Hotkey;
using GameImpact.Abstractions.Input;
using GameImpact.Abstractions.Recognition;
using GameImpact.Automation;
using GameImpact.Capture;
using GameImpact.Hotkey;
using GameImpact.Input;
using GameImpact.OCR;
using GameImpact.Recognition;
using GameImpact.Utilities.Logging;

namespace GameImpact.Core;

public class GameContext : IDisposable
{
    public nint WindowHandle { get; private set; }
    public IScreenCapture Capture { get; private set; } = null!;
    public IInputSimulator Input { get; }
    public IHotkeyService Hotkey { get; }
    public IRecognitionService Recognition { get; }
    public IOcrEngine Ocr { get; }
    public TaskEngine TaskEngine { get; }

    public bool IsInitialized => WindowHandle != nint.Zero;

    public GameContext()
    {
        Input = InputFactory.CreateSendInput();
        Hotkey = new HotkeyService();
        Recognition = new RecognitionService();
        Ocr = new WindowsOcrEngine();
        TaskEngine = new TaskEngine();
        Log.Debug("[GameContext] Created");
    }

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
        bool enableHdr = options?.EnableHdr ?? Direct3D11Helper.IsHdrEnabledForWindow(windowHandle);
        Log.Info("[GameContext] HDR mode: {HdrEnabled} (auto-detected: {AutoDetected})",
            enableHdr, options?.EnableHdr == null);

        Capture = CaptureFactory.Create(enableHdr, useGpuHdrConversion);
        Capture.Start(windowHandle, options);

        // 设置后台输入目标窗口（用于 BackgroundClickAt 等 PostMessage 操作）
        Input.SetWindowHandle(windowHandle);

        TaskEngine.SetCapture(Capture);

        Log.Info("[GameContext] Initialized successfully");
    }

    public void Dispose()
    {
        Log.Debug("[GameContext] Disposing");
        TaskEngine.Dispose();
        Capture?.Dispose();
        Hotkey.Dispose();
        Ocr.Dispose();
        Log.Info("[GameContext] Disposed");
        GC.SuppressFinalize(this);
    }
}
