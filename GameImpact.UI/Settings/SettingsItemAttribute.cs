#region

using System;

#endregion

namespace GameImpact.UI.Settings
{
    /// <summary>设置项控件类型</summary>
    public enum SettingsControlType
    {
        /// <summary>自动推断：bool → 开关, string → 文本框, int/double → 数字输入, Enum → 下拉框</summary>
        Auto,

        /// <summary>复选框 / 开关</summary>
        Toggle,

        /// <summary>单行文本框</summary>
        TextBox,

        /// <summary>下拉选择框</summary>
        ComboBox,

        /// <summary>滑动条</summary>
        Slider,

        /// <summary>复选框组（用于 Flags 枚举的多选）</summary>
        CheckBoxGroup
    }
    /// <summary>标注在 Settings 模型的属性上，描述该设置项在 UI 中的显示方式。 未标注此 Attribute 的属性不会出现在自动生成的设置界面中。</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class SettingsItemAttribute : Attribute
    {
        /// <summary>创建设置项 Attribute</summary>
        /// <param name="displayName">设置项的显示名称</param>
        public SettingsItemAttribute(string displayName)
        {
            DisplayName = displayName;
        }
        /// <summary>设置项的显示名称</summary>
        public string DisplayName{ get; }

        /// <summary>设置项的描述说明文字</summary>
        public string Description{ get; set; } = string.Empty;

        /// <summary>控件类型，默认自动推断</summary>
        public SettingsControlType ControlType{ get; set; } = SettingsControlType.Auto;

        /// <summary>数值最小值（仅对 NumberBox / Slider 有效）</summary>
        public double Min{ get; set; } = double.NaN;

        /// <summary>数值最大值（仅对 NumberBox / Slider 有效）</summary>
        public double Max{ get; set; } = double.NaN;

        /// <summary>下拉框选项列表（用 '|' 分隔 "显示文本:值" 对），仅对 ComboBox 有效。 格式示例："深色:Dark|浅色:Light" 或 "Debug|Information|Warning|Error"</summary>
        public string Options{ get; set; } = string.Empty;

        /// <summary>设置项在同一分组内的排序权重（数值越小越靠前），默认为 0</summary>
        public int Order{ get; set; } = 0;
    }
}
