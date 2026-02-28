using System;
using System.Collections.Generic;
using GameImpact.UI.Views;
using OpenCvSharp;

namespace GameImpact.UI.Services
{
    /// <summary>Overlay 相关 UI 功能的抽象接口。</summary>
    public interface IOverlayUiService
    {
        void AttachTo(nint hwnd);
        void Detach();
        void ForceClose();

        void StartPickCoord(Action<int, int> onPicked);
        void StartScreenshotRegion(Action<int, int, int, int> onComplete);

        void DrawClickMarker(int x, int y, bool success);
        void DrawOcrResult(Rect roi, List<(Rect box, string text)> results);

        void ShowInfo(string key, string text);
        void HideInfo(string key);
    }

    /// <summary>基于 OverlayWindow 单例的默认实现。</summary>
    public sealed class OverlayUiService : IOverlayUiService
    {
        public static OverlayUiService Instance { get; } = new();

        private OverlayWindow Overlay => OverlayWindow.Instance;

        public void AttachTo(nint hwnd) => Overlay.AttachTo(hwnd);

        public void Detach() => Overlay.Detach();

        public void ForceClose() => Overlay.ForceClose();

        public void StartPickCoord(Action<int, int> onPicked) => Overlay.StartPickCoord(onPicked);

        public void StartScreenshotRegion(Action<int, int, int, int> onComplete) => Overlay.StartScreenshotRegion(onComplete);

        public void DrawClickMarker(int x, int y, bool success) => Overlay.DrawClickMarker(x, y, success);

        public void DrawOcrResult(Rect roi, List<(Rect box, string text)> results) => Overlay.DrawOcrResult(roi, results);

        public void ShowInfo(string key, string text) => Overlay.ShowInfo(key, text);

        public void HideInfo(string key) => Overlay.HideInfo(key);
    }
}

