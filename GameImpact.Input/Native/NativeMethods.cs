using System.Runtime.InteropServices;

namespace GameImpact.Input.Native;

/// <summary>
/// 封装 Win32 User32.dll 原生 API 的 P/Invoke 声明。
/// 提供窗口消息发送、坐标转换、输入模拟、窗口管理等底层能力。
/// </summary>
internal static partial class NativeMethods
{
    // =============================================
    //  窗口消息与查找
    // =============================================

    /// <summary>
    /// 根据窗口类名和/或窗口标题查找顶层窗口。
    /// </summary>
    /// <param name="lpClassName">目标窗口的类名，传 null 则忽略类名匹配。</param>
    /// <param name="lpWindowName">目标窗口的标题文本，传 null 则忽略标题匹配。</param>
    /// <returns>找到的窗口句柄；未找到时返回 <see cref="nint.Zero"/>。</returns>
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindow(string? lpClassName, string? lpWindowName);

    /// <summary>
    /// 向指定窗口投递（Post）一条异步消息，不等待目标窗口处理即返回。
    /// 适用于不需要等待返回结果的输入模拟场景（如按键、鼠标事件）。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="msg">要发送的窗口消息（如 WM_KEYDOWN）。</param>
    /// <param name="wParam">消息附加参数，含义取决于具体消息类型。</param>
    /// <param name="lParam">消息附加参数，含义取决于具体消息类型。</param>
    /// <returns>成功返回 true，失败返回 false。</returns>
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>
    /// 向指定窗口发送（Send）一条同步消息，阻塞等待目标窗口处理完毕后返回。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="msg">要发送的窗口消息。</param>
    /// <param name="wParam">消息附加参数。</param>
    /// <param name="lParam">消息附加参数。</param>
    /// <returns>消息处理结果，具体含义取决于消息类型。</returns>
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", SetLastError = true)]
    public static partial nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    // =============================================
    //  坐标转换
    // =============================================

    /// <summary>
    /// 将屏幕坐标转换为指定窗口的客户区坐标。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="lpPoint">输入为屏幕坐标，输出为窗口客户区坐标（引用传递，原地修改）。</param>
    /// <returns>成功返回 true，失败返回 false。</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint hWnd, ref Point lpPoint);

    /// <summary>
    /// 将指定窗口的客户区坐标转换为屏幕坐标。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="lpPoint">输入为窗口客户区坐标，输出为屏幕坐标（引用传递，原地修改）。</param>
    /// <returns>成功返回 true，失败返回 false。</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hWnd, ref Point lpPoint);

    // =============================================
    //  输入模拟
    // =============================================

    /// <summary>
    /// 将键盘/鼠标输入事件合成注入到输入流中，等同于真实的物理输入。
    /// 这些事件会被发送到当前拥有键盘/鼠标焦点的窗口。
    /// </summary>
    /// <param name="nInputs">pInputs 数组中的输入事件数量。</param>
    /// <param name="pInputs">输入事件数组，每个元素描述一个键盘或鼠标事件。</param>
    /// <param name="cbSize">单个 <see cref="Input"/> 结构体的字节大小，通常为 <c>Marshal.SizeOf&lt;Input&gt;()</c>。</param>
    /// <returns>成功插入到输入流中的事件数量；返回 0 表示发送失败（例如被 UIPI 安全机制阻止）。</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Input[] pInputs, int cbSize);

    /// <summary>
    /// 将虚拟键码映射为扫描码，或将扫描码映射为虚拟键码。
    /// </summary>
    /// <param name="uCode">要映射的虚拟键码或扫描码。</param>
    /// <param name="uMapType">映射类型：0 = 虚拟键→扫描码, 1 = 扫描码→虚拟键, 2 = 虚拟键→字符值, 3 = 扫描码→虚拟键(区分左右)。</param>
    /// <returns>映射后的值；若无有效映射则返回 0。</returns>
    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW", SetLastError = true)]
    public static partial uint MapVirtualKey(uint uCode, uint uMapType);

    /// <summary>
    /// 全局键盘模拟（旧版 API），将按键事件发送给当前前台焦点窗口（等同真实按键）。
    /// 推荐优先使用 <see cref="SendInput"/>，此方法保留用于兼容性场景。
    /// </summary>
    /// <param name="bVk">虚拟键码（Virtual-Key Code），如 0x41 表示 'A' 键。</param>
    /// <param name="bScan">硬件扫描码，可通过 <see cref="MapVirtualKey"/> 获取。</param>
    /// <param name="dwFlags">标志位：0 = 按下, KEYEVENTF_KEYUP (0x0002) = 释放。</param>
    /// <param name="dwExtraInfo">与按键事件关联的附加信息，通常传 <see cref="UIntPtr.Zero"/>。</param>
    [LibraryImport("user32.dll")]
    public static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>
    /// 获取鼠标光标在屏幕上的当前位置（屏幕坐标）。
    /// </summary>
    /// <param name="lpPoint">输出参数，接收光标的屏幕坐标。</param>
    /// <returns>成功返回 true，失败返回 false。</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point lpPoint);

    /// <summary>
    /// 将鼠标光标移动到屏幕上的指定位置。
    /// </summary>
    /// <param name="x">目标位置的屏幕 X 坐标。</param>
    /// <param name="y">目标位置的屏幕 Y 坐标。</param>
    /// <returns>成功返回 true，失败返回 false。</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    // =============================================
    //  窗口管理
    // =============================================

    /// <summary>
    /// 将指定窗口设置为前台窗口（激活并获得焦点）。
    /// 注意：系统对此 API 有限制，只有满足特定条件的进程才能成功调用。
    /// </summary>
    /// <param name="hWnd">要激活的窗口句柄。</param>
    /// <returns>窗口成功被激活返回 true，否则返回 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    /// <summary>
    /// 判断指定窗口是否处于最小化（图标化）状态。
    /// </summary>
    /// <param name="hWnd">要检查的窗口句柄。</param>
    /// <returns>窗口已最小化返回 true，否则返回 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hWnd);

    /// <summary>
    /// 控制窗口的显示状态（显示、隐藏、最大化、最小化、还原等）。
    /// </summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="nCmdShow">显示命令，如 <see cref="SW_Restore"/> (9) 表示还原窗口。</param>
    /// <returns>窗口之前是否可见：之前可见返回 true，之前隐藏返回 false。</returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    /// <summary>ShowWindow 命令：还原被最小化的窗口到其之前的大小和位置。</summary>
    public const int SW_Restore = 9;

    // =============================================
    //  结构体定义
    // =============================================

    /// <summary>
    /// 表示一个二维坐标点（对应 Win32 POINT 结构体）。
    /// 用于屏幕坐标与客户区坐标的相互转换。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Point(int x, int y)
    {
        /// <summary>水平坐标（像素）。</summary>
        public int X = x;
        /// <summary>垂直坐标（像素）。</summary>
        public int Y = y;
    }

    /// <summary>
    /// 描述一个输入事件（对应 Win32 INPUT 结构体）。
    /// 通过 <see cref="Type"/> 字段区分键盘输入、鼠标输入或硬件输入。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Input(uint type, InputUnion union)
    {
        /// <summary>输入类型：<see cref="InputMouse"/> (0) = 鼠标, <see cref="InputKeyboard"/> (1) = 键盘。</summary>
        public uint Type = type;
        /// <summary>包含具体输入数据的联合体。</summary>
        public InputUnion Union = union;
        /// <summary>获取此结构体的内存大小（字节），用作 <see cref="SendInput"/> 的 cbSize 参数。</summary>
        public static int Size => Marshal.SizeOf<Input>();
    }

    /// <summary>
    /// 输入联合体（对应 Win32 INPUT 中的匿名 union），
    /// 使用 FieldOffset(0) 使三种输入类型共享同一块内存。
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        /// <summary>鼠标输入数据（当 Input.Type == <see cref="InputMouse"/> 时有效）。</summary>
        [FieldOffset(0)] public MouseInput Mouse;
        /// <summary>键盘输入数据（当 Input.Type == <see cref="InputKeyboard"/> 时有效）。</summary>
        [FieldOffset(0)] public KeyboardInput Keyboard;
        /// <summary>硬件输入数据（一般很少使用）。</summary>
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    /// <summary>
    /// 鼠标输入事件数据（对应 Win32 MOUSEINPUT 结构体）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        /// <summary>鼠标水平方向的移动量或绝对 X 坐标（取决于 Flags）。</summary>
        public int Dx;
        /// <summary>鼠标垂直方向的移动量或绝对 Y 坐标（取决于 Flags）。</summary>
        public int Dy;
        /// <summary>附加数据：滚轮事件时为滚动量（正值向上，负值向下，单位 WHEEL_DELTA=120）。</summary>
        public uint MouseData;
        /// <summary>鼠标事件标志位，由 MouseEventFlags_* 常量组合（如移动、按下、释放、滚轮等）。</summary>
        public uint Flags;
        /// <summary>事件的时间戳（毫秒）。设为 0 时系统自动填充。</summary>
        public uint Time;
        /// <summary>与事件关联的附加信息，通常为 <see cref="UIntPtr.Zero"/>。</summary>
        public UIntPtr ExtraInfo;
    }

    /// <summary>
    /// 键盘输入事件数据（对应 Win32 KEYBDINPUT 结构体）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        /// <summary>虚拟键码（Virtual-Key Code）。若 Flags 包含 KEYEVENTF_UNICODE 则设为 0。</summary>
        public ushort Vk;
        /// <summary>硬件扫描码。可通过 <see cref="MapVirtualKey"/> 从虚拟键码转换而来。</summary>
        public ushort Scan;
        /// <summary>键盘事件标志位，由 KeyEventFlags_* 常量组合（按下/释放/扩展键/扫描码/Unicode）。</summary>
        public uint Flags;
        /// <summary>事件的时间戳（毫秒）。设为 0 时系统自动填充。</summary>
        public uint Time;
        /// <summary>与事件关联的附加信息，通常为 <see cref="UIntPtr.Zero"/>。</summary>
        public UIntPtr ExtraInfo;
    }

    /// <summary>
    /// 硬件输入事件数据（对应 Win32 HARDWAREINPUT 结构体），一般很少使用。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HardwareInput
    {
        /// <summary>由输入硬件生成的消息。</summary>
        public uint Msg;
        /// <summary>消息的低 16 位参数。</summary>
        public ushort ParamL;
        /// <summary>消息的高 16 位参数。</summary>
        public ushort ParamH;
    }

    // =============================================
    //  输入类型常量
    // =============================================

    /// <summary>INPUT 结构体类型：鼠标输入事件。</summary>
    public const uint InputMouse = 0;
    /// <summary>INPUT 结构体类型：键盘输入事件。</summary>
    public const uint InputKeyboard = 1;

    // =============================================
    //  鼠标事件标志 (MOUSEEVENTF_*)
    // =============================================

    /// <summary>鼠标发生了移动。</summary>
    public const uint MouseEventFlags_Move = 0x0001;
    /// <summary>鼠标左键按下。</summary>
    public const uint MouseEventFlags_LeftDown = 0x0002;
    /// <summary>鼠标左键释放。</summary>
    public const uint MouseEventFlags_LeftUp = 0x0004;
    /// <summary>鼠标右键按下。</summary>
    public const uint MouseEventFlags_RightDown = 0x0008;
    /// <summary>鼠标右键释放。</summary>
    public const uint MouseEventFlags_RightUp = 0x0010;
    /// <summary>鼠标中键按下。</summary>
    public const uint MouseEventFlags_MiddleDown = 0x0020;
    /// <summary>鼠标中键释放。</summary>
    public const uint MouseEventFlags_MiddleUp = 0x0040;
    /// <summary>鼠标滚轮滚动，滚动量由 MouseData 字段指定。</summary>
    public const uint MouseEventFlags_Wheel = 0x0800;

    // =============================================
    //  键盘事件标志 (KEYEVENTF_*)
    // =============================================

    /// <summary>按键按下（值为 0，即无特殊标志时默认为按下）。</summary>
    public const uint KeyEventFlags_KeyDown = 0x0000;
    /// <summary>扩展键标志，用于区分某些物理位置不同但虚拟键码相同的按键（如右 Alt、右 Ctrl、小键盘区）。</summary>
    public const uint KeyEventFlags_ExtendedKey = 0x0001;
    /// <summary>按键释放。</summary>
    public const uint KeyEventFlags_KeyUp = 0x0002;
    /// <summary>使用硬件扫描码（Scan 字段）而非虚拟键码来标识按键。</summary>
    public const uint KeyEventFlags_Scancode = 0x0008;
    /// <summary>发送 Unicode 字符事件，Scan 字段包含 UTF-16 字符值，Vk 应设为 0。</summary>
    public const uint KeyEventFlags_Unicode = 0x0004;

    // =============================================
    //  窗口消息常量 (WM_*)
    // =============================================

    /// <summary>WM_ACTIVATE (0x0006)：窗口激活/失活通知。wParam 低位字为激活状态。</summary>
    public const uint WindowMessage_Activate = 0x0006;
    /// <summary>WM_KEYDOWN (0x0100)：非系统键按下。wParam = 虚拟键码，lParam = 按键详细信息。</summary>
    public const uint WindowMessage_KeyDown = 0x0100;
    /// <summary>WM_KEYUP (0x0101)：非系统键释放。wParam = 虚拟键码，lParam = 按键详细信息。</summary>
    public const uint WindowMessage_KeyUp = 0x0101;
    /// <summary>WM_CHAR (0x0102)：字符输入消息（由 TranslateMessage 从 WM_KEYDOWN 翻译而来）。wParam = 字符编码。</summary>
    public const uint WindowMessage_Char = 0x0102;
    /// <summary>WM_MOUSEMOVE (0x0200)：鼠标在窗口客户区内移动。lParam = 坐标（由 MakeLParam 打包）。</summary>
    public const uint WindowMessage_MouseMove = 0x0200;
    /// <summary>WM_LBUTTONDOWN (0x0201)：鼠标左键在窗口客户区内按下。</summary>
    public const uint WindowMessage_LeftButtonDown = 0x0201;
    /// <summary>WM_LBUTTONUP (0x0202)：鼠标左键在窗口客户区内释放。</summary>
    public const uint WindowMessage_LeftButtonUp = 0x0202;
    /// <summary>WM_RBUTTONDOWN (0x0204)：鼠标右键在窗口客户区内按下。</summary>
    public const uint WindowMessage_RButtonDown = 0x0204;
    /// <summary>WM_RBUTTONUP (0x0205)：鼠标右键在窗口客户区内释放。</summary>
    public const uint WindowMessage_RButtonUp = 0x0205;

    // =============================================
    //  鼠标键状态（用于 wParam 中指示伴随的按键状态）
    // =============================================

    /// <summary>MK_LBUTTON (0x0001)：鼠标左键处于按下状态，常作为鼠标消息 wParam 的一部分。</summary>
    public const nint MouseKey_LeftButton = 0x0001;

    // =============================================
    //  工具方法
    // =============================================

    /// <summary>
    /// 将客户区坐标 (x, y) 打包为窗口消息的 lParam 格式。
    /// 低 16 位存放 x 坐标，高 16 位存放 y 坐标。
    /// 用于构造 WM_MOUSEMOVE、WM_LBUTTONDOWN 等鼠标消息的 lParam 参数。
    /// </summary>
    /// <param name="x">客户区 X 坐标。</param>
    /// <param name="y">客户区 Y 坐标。</param>
    /// <returns>打包后的 lParam 值。</returns>
    public static nint MakeLParam(int x, int y) => ((ushort)y << 16) | (ushort)x;
}
