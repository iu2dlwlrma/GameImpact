#region

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

#endregion

namespace GameImpact.UI.Views
{
    /// <summary>截图命名对话框，包含图片预览</summary>
    public partial class ScreenshotNameDialog : System.Windows.Window
    {
        private readonly Mat m_screenshot;
        private readonly string m_defaultFileName;

        /// <summary>构造函数</summary>
        /// <param name="screenshot">截图的 Mat 对象</param>
        /// <param name="defaultFileName">默认文件名（不含扩展名）</param>
        public ScreenshotNameDialog(Mat screenshot, string defaultFileName)
        {
            InitializeComponent();
            m_screenshot = screenshot;
            m_defaultFileName = defaultFileName;

            // 设置默认文件名
            FileNameBox.Text = m_defaultFileName;
            FileNameBox.SelectAll();
            FileNameBox.Focus();

            // 加载预览图片
            LoadPreview();

            // 监听回车键
            FileNameBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && SaveButton.IsEnabled)
                {
                    SaveButton_Click(sender: null, e: null);
                }
            };
        }

        /// <summary>保存的文件路径</summary>
        public string? SavedFilePath { get; private set; }

        /// <summary>标题栏鼠标左键按下事件处理</summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        /// <summary>加载预览图片</summary>
        private void LoadPreview()
        {
            try
            {
                if (m_screenshot == null || m_screenshot.Empty())
                {
                    return;
                }

                // 将 Mat 转换为 BitmapSource
                var bitmapSource = MatToBitmapSource(m_screenshot);
                PreviewImage.Source = bitmapSource;
            }
            catch (Exception ex)
            {
                // 预览加载失败不影响保存
                System.Diagnostics.Debug.WriteLine($"[ScreenshotNameDialog] 预览加载失败: {ex.Message}");
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
                // OpenCvSharp Mat 默认是 BGR，需要转换为 BGRA
                if (mat.Channels() == 3)
                {
                    rgba = new Mat();
                    Cv2.CvtColor(mat, rgba, ColorConversionCodes.BGR2BGRA);
                }
                else if (mat.Channels() == 4)
                {
                    // 已经是 BGRA，直接使用
                    rgba = mat;
                }
                else
                {
                    // 灰度图转换为 BGRA
                    rgba = new Mat();
                    Cv2.CvtColor(mat, rgba, ColorConversionCodes.GRAY2BGRA);
                }

                var width = rgba.Width;
                var height = rgba.Height;
                var stride = rgba.Step();
                
                // 将 Mat 数据复制到字节数组
                var data = new byte[height * stride];
                Marshal.Copy(rgba.Data, data, 0, data.Length);

                var bitmap = BitmapSource.Create(
                    width, height,
                    96, 96, // DPI
                    PixelFormats.Bgra32,
                    null,
                    data,
                    (int)stride);

                // 冻结 BitmapSource 以便跨线程使用，并确保数据被复制
                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                // 只释放新创建的 Mat，不释放原始 mat
                if (rgba != null && rgba != mat)
                {
                    rgba.Dispose();
                }
            }
        }

        /// <summary>文件名输入框文本变更事件处理</summary>
        private void FileNameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ValidateFileName();
        }

        /// <summary>验证文件名</summary>
        private void ValidateFileName()
        {
            var fileName = FileNameBox.Text?.Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                SaveButton.IsEnabled = false;
                return;
            }

            // 检查文件名是否包含非法字符
            var invalidChars = Path.GetInvalidFileNameChars();
            if (fileName.IndexOfAny(invalidChars) >= 0)
            {
                SaveButton.IsEnabled = false;
                return;
            }

            SaveButton.IsEnabled = true;
        }

        /// <summary>保存按钮点击事件处理</summary>
        private void SaveButton_Click(object? sender, RoutedEventArgs? e)
        {
            var fileName = FileNameBox.Text?.Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                return;
            }

            // 确保文件名有扩展名
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".png";
            }

            SavedFilePath = fileName;
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
