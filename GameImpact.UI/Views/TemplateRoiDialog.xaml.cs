#region

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

#endregion

namespace GameImpact.UI.Views
{
    /// <summary>模板ROI设置对话框，支持可视化框选匹配区域和文字区域</summary>
    public partial class TemplateRoiDialog : System.Windows.Window
    {
        private System.Windows.Point m_startPoint;
        private bool m_isSelecting;
        private bool m_isSelectingMatchRoi = true; // true=匹配区域, false=文字区域
        private System.Windows.Rect? m_matchRoi;
        private System.Windows.Rect? m_textRoi;
        private readonly string m_templatePath;

        /// <summary>构造函数</summary>
        /// <param name="templatePath">模板图片路径</param>
        /// <param name="matchRoi">初始匹配区域ROI（相对于模板图片）</param>
        /// <param name="textRoi">初始文字区域ROI（相对于模板图片）</param>
        public TemplateRoiDialog(string templatePath, OpenCvSharp.Rect? matchRoi = null, OpenCvSharp.Rect? textRoi = null)
        {
            InitializeComponent();
            m_templatePath = templatePath;
            m_matchRoi = matchRoi.HasValue 
                ? new System.Windows.Rect(matchRoi.Value.X, matchRoi.Value.Y, matchRoi.Value.Width, matchRoi.Value.Height)
                : null;
            m_textRoi = textRoi.HasValue
                ? new System.Windows.Rect(textRoi.Value.X, textRoi.Value.Y, textRoi.Value.Width, textRoi.Value.Height)
                : null;

            LoadTemplateImage();
            UpdateRoiDisplay();
        }

        /// <summary>匹配区域ROI（相对于模板图片）</summary>
        public OpenCvSharp.Rect? MatchRoi => m_matchRoi.HasValue 
            ? new OpenCvSharp.Rect((int)m_matchRoi.Value.X, (int)m_matchRoi.Value.Y, (int)m_matchRoi.Value.Width, (int)m_matchRoi.Value.Height)
            : null;

        /// <summary>文字区域ROI（相对于模板图片）</summary>
        public OpenCvSharp.Rect? TextRoi => m_textRoi.HasValue
            ? new OpenCvSharp.Rect((int)m_textRoi.Value.X, (int)m_textRoi.Value.Y, (int)m_textRoi.Value.Width, (int)m_textRoi.Value.Height)
            : null;

        /// <summary>标题栏鼠标左键按下事件处理</summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        /// <summary>加载模板图片</summary>
        private void LoadTemplateImage()
        {
            try
            {
                if (!File.Exists(m_templatePath))
                {
                    return;
                }

                using var mat = Cv2.ImRead(m_templatePath);
                if (mat.Empty())
                {
                    return;
                }

                var bitmapSource = MatToBitmapSource(mat);
                TemplateImage.Source = bitmapSource;

                // 设置Canvas大小
                ImageCanvas.Width = mat.Width;
                ImageCanvas.Height = mat.Height;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TemplateRoiDialog] 加载图片失败: {ex.Message}");
            }
        }

        /// <summary>将 Mat 转换为 BitmapSource</summary>
        private BitmapSource MatToBitmapSource(Mat mat)
        {
            if (mat.Empty())
            {
                return null!;
            }

            Mat? rgba = null;
            try
            {
                if (mat.Channels() == 3)
                {
                    rgba = new Mat();
                    Cv2.CvtColor(mat, rgba, ColorConversionCodes.BGR2BGRA);
                }
                else if (mat.Channels() == 4)
                {
                    rgba = mat;
                }
                else
                {
                    rgba = new Mat();
                    Cv2.CvtColor(mat, rgba, ColorConversionCodes.GRAY2BGRA);
                }

                var width = rgba.Width;
                var height = rgba.Height;
                var stride = rgba.Step();

                var data = new byte[height * stride];
                Marshal.Copy(rgba.Data, data, 0, data.Length);

                var bitmap = BitmapSource.Create(
                    width, height,
                    96, 96,
                    PixelFormats.Bgra32,
                    null,
                    data,
                    (int)stride);

                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                if (rgba != null && rgba != mat)
                {
                    rgba.Dispose();
                }
            }
        }

        /// <summary>Canvas鼠标按下事件处理</summary>
        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 检查点击的是哪个区域
            var pos = e.GetPosition(ImageCanvas);
            
            // 如果按住Ctrl键，选择文字区域；否则选择匹配区域
            m_isSelectingMatchRoi = !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl);
            
            m_startPoint = pos;
            m_isSelecting = true;
            ImageCanvas.CaptureMouse();
            e.Handled = true;
        }

        /// <summary>Canvas鼠标移动事件处理</summary>
        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!m_isSelecting)
            {
                return;
            }

            var currentPoint = e.GetPosition(ImageCanvas);
            UpdateSelectionRect(m_startPoint, currentPoint);
        }

        /// <summary>Canvas鼠标释放事件处理</summary>
        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!m_isSelecting)
            {
                return;
            }

            ImageCanvas.ReleaseMouseCapture();
            m_isSelecting = false;

            var endPoint = e.GetPosition(ImageCanvas);
            var rect = CreateRect(m_startPoint, endPoint);

            if (rect.Width > 5 && rect.Height > 5) // 最小尺寸检查
            {
                if (m_isSelectingMatchRoi)
                {
                    m_matchRoi = rect;
                }
                else
                {
                    m_textRoi = rect;
                }
                UpdateRoiDisplay();
            }
        }

        /// <summary>更新选择矩形显示</summary>
        private void UpdateSelectionRect(System.Windows.Point start, System.Windows.Point end)
        {
            var rect = CreateRect(start, end);
            var roiRect = m_isSelectingMatchRoi ? MatchRoiRect : TextRoiRect;

            Canvas.SetLeft(roiRect, rect.X);
            Canvas.SetTop(roiRect, rect.Y);
            roiRect.Width = rect.Width;
            roiRect.Height = rect.Height;
            roiRect.Visibility = Visibility.Visible;
        }

        /// <summary>创建矩形（确保左上角在左上方）</summary>
        private System.Windows.Rect CreateRect(System.Windows.Point p1, System.Windows.Point p2)
        {
            var x = Math.Min(p1.X, p2.X);
            var y = Math.Min(p1.Y, p2.Y);
            var width = Math.Abs(p2.X - p1.X);
            var height = Math.Abs(p2.Y - p1.Y);

            // 限制在Canvas范围内
            x = Math.Max(0, Math.Min(x, ImageCanvas.Width - 1));
            y = Math.Max(0, Math.Min(y, ImageCanvas.Height - 1));
            width = Math.Min(width, ImageCanvas.Width - x);
            height = Math.Min(height, ImageCanvas.Height - y);

            return new System.Windows.Rect(x, y, width, height);
        }

        /// <summary>更新ROI显示</summary>
        private void UpdateRoiDisplay()
        {
            // 更新匹配区域显示
            if (m_matchRoi.HasValue)
            {
                var roi = m_matchRoi.Value;
                Canvas.SetLeft(MatchRoiRect, roi.X);
                Canvas.SetTop(MatchRoiRect, roi.Y);
                MatchRoiRect.Width = roi.Width;
                MatchRoiRect.Height = roi.Height;
                MatchRoiRect.Visibility = Visibility.Visible;
                MatchRoiInfo.Text = $"X:{roi.X:F0} Y:{roi.Y:F0} W:{roi.Width:F0} H:{roi.Height:F0}";
            }
            else
            {
                MatchRoiRect.Visibility = Visibility.Collapsed;
                MatchRoiInfo.Text = "未设置";
            }

            // 更新文字区域显示
            if (m_textRoi.HasValue)
            {
                var roi = m_textRoi.Value;
                Canvas.SetLeft(TextRoiRect, roi.X);
                Canvas.SetTop(TextRoiRect, roi.Y);
                TextRoiRect.Width = roi.Width;
                TextRoiRect.Height = roi.Height;
                TextRoiRect.Visibility = Visibility.Visible;
                TextRoiInfo.Text = $"X:{roi.X:F0} Y:{roi.Y:F0} W:{roi.Width:F0} H:{roi.Height:F0}";
            }
            else
            {
                TextRoiRect.Visibility = Visibility.Collapsed;
                TextRoiInfo.Text = "未设置";
            }
        }

        /// <summary>清除匹配区域</summary>
        private void ClearMatchRoi_Click(object sender, RoutedEventArgs e)
        {
            m_matchRoi = null;
            UpdateRoiDisplay();
        }

        /// <summary>清除文字区域</summary>
        private void ClearTextRoi_Click(object sender, RoutedEventArgs e)
        {
            m_textRoi = null;
            UpdateRoiDisplay();
        }

        /// <summary>确定按钮点击事件处理</summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        /// <summary>取消按钮点击事件处理</summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
