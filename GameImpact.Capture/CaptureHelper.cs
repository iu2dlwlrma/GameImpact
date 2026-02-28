#region

using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

#endregion

namespace GameImpact.Capture
{
    /// <summary>Windows.Graphics.Capture 辅助类，提供窗口捕获项创建功能</summary>
    public static class CaptureHelper
    {
        private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        /// <summary>为指定窗口创建 GraphicsCaptureItem</summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <returns>GraphicsCaptureItem 实例</returns>
        public static GraphicsCaptureItem CreateItemForWindow(nint hWnd)
        {
            var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var iid = GraphicsCaptureItemGuid;
            factory.CreateForWindow(hWnd, ref iid, out var pointer);
            return GraphicsCaptureItem.FromAbi(pointer);
        }

        [ComImport] [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")] [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            int CreateForWindow([In] nint window, [In] ref Guid iid, out nint result);
            int CreateForMonitor([In] nint monitor, [In] ref Guid iid, out nint result);
        }
    }
}
