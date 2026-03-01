#region

using System.Windows;
using System.Windows.Media;

#endregion

namespace GameImpact.Utilities.UI
{
    /// <summary>通用的UI处理工具。</summary>
    public static class UIHelper
    {
        /// <summary>查找父元素</summary>
        public static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            var parentObject = child;
            while (parentObject != null)
            {
                if (parentObject is T parent)
                {
                    return parent;
                }
                parentObject = VisualTreeHelper.GetParent(parentObject);
            }
            return null;
        }
    }
}
