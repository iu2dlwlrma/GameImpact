#region

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GameImpact.Utilities.Logging;

#endregion

namespace GameImpact.UI.Settings
{
    /// <summary>通用设置视图：通过反射 Settings 模型上的 Attribute 自动生成 UI 控件。 反射元数据通过 SettingsMetadataCache 缓存，只解析一次。</summary>
    /// <typeparam name="T">设置模型类型</typeparam>
    public class AutoSettingsView<T> : UserControl where T : class, new()
    {
        /// <summary>缓存的控件映射：PropertyInfo → 对应的 UI 控件，用于快速读写值</summary>
        private readonly Dictionary<PropertyInfo, FrameworkElement> m_controlMap = new();
        private readonly T m_settings;
        private readonly ISettingsProvider<T> m_settingsProvider;

        /// <summary>指定要显示的分组列表。为 null 时显示所有分组。</summary>
        private readonly List<SettingsGroupMetadata>? m_targetGroups;
        private bool m_isLoading;

        /// <summary>创建设置视图，显示所有分组</summary>
        public AutoSettingsView(ISettingsProvider<T> settingsProvider)
                : this(settingsProvider, null)
        {
        }

        /// <summary>创建设置视图，仅显示指定的分组</summary>
        /// <param name="settingsProvider">设置持久化提供者</param>
        /// <param name="targetGroups">要显示的分组列表，为 null 则显示全部</param>
        public AutoSettingsView(ISettingsProvider<T> settingsProvider, List<SettingsGroupMetadata>? targetGroups)
        {
            m_settingsProvider = settingsProvider;
            m_settings = m_settingsProvider.Load();
            m_targetGroups = targetGroups;
            BuildUi();
            LoadSettingsToUi();
        }

        /// <summary>设置项变更回调，外部可以监听以执行即时生效逻辑（如主题切换）</summary>
        public event Action<T, string>? SettingChanged;

        /// <summary>获取当前设置模型的所有分组元数据</summary>
        public static List<SettingsGroupMetadata> GetAllGroups()
        {
            return SettingsMetadataCache.GetGroups<T>();
        }

        /// <summary>根据缓存的元数据构建 UI 布局</summary>
        private void BuildUi()
        {
            var scrollViewer = new ScrollViewer
            {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(0, 0, 8, 0)
            };

            var rootPanel = new StackPanel { Margin = new Thickness(4) };

            var groups = m_targetGroups ?? SettingsMetadataCache.GetGroups<T>();

            foreach (var group in groups)
            {
                // 分组标题（无分组名时不显示标题）
                if (!string.IsNullOrEmpty(group.GroupName))
                {
                    var groupHeader = new TextBlock
                    {
                            FontSize = 14,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 0, 0, 12)
                    };
                    groupHeader.SetResourceReference(TextBlock.ForegroundProperty, "AppText");

                    var headerText = string.IsNullOrEmpty(group.Icon) ? group.GroupName : $"{group.Icon} {group.GroupName}";
                    groupHeader.Text = headerText;

                    rootPanel.Children.Add(groupHeader);
                }

                // 分组卡片容器
                var card = new Border
                {
                        CornerRadius = new CornerRadius(8),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(16, 12, 16, 12),
                        Margin = new Thickness(0, 0, 0, 16)
                };
                card.SetResourceReference(Border.BackgroundProperty, "AppSurface");
                card.SetResourceReference(Border.BorderBrushProperty, "AppBorderSubtle");

                var cardPanel = new StackPanel();

                for (var i = 0; i < group.Items.Count; i++)
                {
                    var meta = group.Items[i];

                    // 分隔线（非首项）
                    if (i > 0)
                    {
                        var separator = new Border
                        {
                                Height = 1,
                                Margin = new Thickness(0, 0, 0, 12)
                        };
                        separator.SetResourceReference(Border.BackgroundProperty, "AppBorderSubtle");
                        cardPanel.Children.Add(separator);
                    }

                    // 设置项行
                    var itemRow = CreateSettingsItemRow(meta, i == 0, i == group.Items.Count - 1);
                    cardPanel.Children.Add(itemRow);
                }

                card.Child = cardPanel;
                rootPanel.Children.Add(card);
            }

            scrollViewer.Content = rootPanel;
            Content = scrollViewer;
        }

        /// <summary>创建单个设置项的行布局（左侧标题+描述，右侧控件）</summary>
        private Grid CreateSettingsItemRow(SettingsItemMetadata meta, bool isFirst, bool isLast)
        {
            var grid = new Grid
            {
                    HorizontalAlignment = HorizontalAlignment.Stretch
            };

            double topMargin = isFirst ? 4 : 0;
            double bottomMargin = isLast ? 4 : 12;
            grid.Margin = new Thickness(0, topMargin, 0, bottomMargin);

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧：标题 + 描述
            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var titleBlock = new TextBlock
            {
                    Text = meta.Item.DisplayName,
                    FontSize = 13
            };
            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppText");
            textPanel.Children.Add(titleBlock);

            if (!string.IsNullOrEmpty(meta.Item.Description))
            {
                var descBlock = new TextBlock
                {
                        Text = meta.Item.Description,
                        FontSize = 11,
                        Margin = new Thickness(0, 2, 0, 0)
                };
                descBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppTextMuted");
                textPanel.Children.Add(descBlock);
            }

            Grid.SetColumn(textPanel, 0);
            grid.Children.Add(textPanel);

            // 右侧：控件
            var control = CreateControl(meta);
            Grid.SetColumn(control, 1);
            control.VerticalAlignment = VerticalAlignment.Center;
            control.HorizontalAlignment = HorizontalAlignment.Right;

            grid.Children.Add(control);

            // 缓存映射
            m_controlMap[meta.Property] = control;

            return grid;
        }

        /// <summary>根据属性类型和 Attribute 配置创建对应的 UI 控件</summary>
        private FrameworkElement CreateControl(SettingsItemMetadata meta)
        {
            var controlType = ResolveControlType(meta);

            return controlType switch
            {
                    SettingsControlType.Toggle => CreateToggleControl(meta),
                    SettingsControlType.TextBox => CreateTextBoxControl(meta),
                    SettingsControlType.ComboBox => CreateComboBoxControl(meta),
                    SettingsControlType.Slider => CreateSliderControl(meta),
                    _ => CreateTextBoxControl(meta)
            };
        }

        /// <summary>推断控件类型</summary>
        private static SettingsControlType ResolveControlType(SettingsItemMetadata meta)
        {
            if (meta.Item.ControlType != SettingsControlType.Auto)
            {
                return meta.Item.ControlType;
            }

            var propType = meta.Property.PropertyType;

            if (propType == typeof(bool))
            {
                return SettingsControlType.Toggle;
            }

            if (propType.IsEnum || (propType == typeof(string) && !string.IsNullOrEmpty(meta.Item.Options)))
            {
                return SettingsControlType.ComboBox;
            }

            if (!double.IsNaN(meta.Item.Min) && !double.IsNaN(meta.Item.Max))
            {
                return SettingsControlType.Slider;
            }

            return SettingsControlType.TextBox;
        }

#region 工具方法

        /// <summary>解析 Options 字符串为 字典（值 → 显示文本）。 格式："显示文本:值|显示文本:值" 或 "值|值"（此时显示文本 = 值）</summary>
        private static Dictionary<string, string> ParseOptions(string options)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(options))
            {
                return result;
            }

            var pairs = options.Split('|', StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in pairs)
            {
                var parts = pair.Split(':', 2);
                if (parts.Length == 2)
                {
                    // "显示文本:值" 格式
                    result[parts[1].Trim()] = parts[0].Trim();
                }
                else
                {
                    // "值" 格式（显示文本 = 值）
                    var value = parts[0].Trim();
                    result[value] = value;
                }
            }

            return result;
        }

#endregion

#region 控件创建

        /// <summary>创建 CheckBox 开关控件</summary>
        private CheckBox CreateToggleControl(SettingsItemMetadata meta)
        {
            var checkBox = new CheckBox();
            checkBox.Checked += (_, _) => OnControlValueChanged(meta.Property);
            checkBox.Unchecked += (_, _) => OnControlValueChanged(meta.Property);

            return checkBox;
        }

        /// <summary>创建文本输入框控件</summary>
        private TextBox CreateTextBoxControl(SettingsItemMetadata meta)
        {
            var textBox = new TextBox
            {
                    Width = 100
            };
            textBox.LostFocus += (_, _) => OnControlValueChanged(meta.Property);

            return textBox;
        }

        /// <summary>创建下拉框控件</summary>
        private ComboBox CreateComboBoxControl(SettingsItemMetadata meta)
        {
            var comboBox = new ComboBox
            {
                    Width = 160,
                    MinHeight = 30,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Focusable = true,
                    IsEditable = false
            };

            var propType = meta.Property.PropertyType;

            if (propType.IsEnum)
            {
                // 枚举类型：如果提供了 Options 则使用自定义显示文本，否则用枚举名
                var enumValues = Enum.GetValues(propType);
                var optionsMap = ParseOptions(meta.Item.Options);

                foreach (var enumValue in enumValues)
                {
                    var name = enumValue?.ToString() ?? string.Empty;
                    var displayText = optionsMap.TryGetValue(name, out var custom) ? custom : name;

                    comboBox.Items.Add(new ComboBoxItem
                    {
                            Content = displayText,
                            Tag = name
                    });
                }
            }
            else if (!string.IsNullOrEmpty(meta.Item.Options))
            {
                // 字符串类型 + Options
                var optionsMap = ParseOptions(meta.Item.Options);

                foreach (var kvp in optionsMap)
                {
                    comboBox.Items.Add(new ComboBoxItem
                    {
                            Content = kvp.Value,
                            Tag = kvp.Key
                    });
                }
            }

            comboBox.SelectionChanged += (_, _) => OnControlValueChanged(meta.Property);

            return comboBox;
        }

        /// <summary>创建滑动条控件</summary>
        private FrameworkElement CreateSliderControl(SettingsItemMetadata meta)
        {
            var panel = new StackPanel
            {
                    Orientation = Orientation.Horizontal
            };

            var slider = new Slider
            {
                    Width = 120,
                    Minimum = double.IsNaN(meta.Item.Min) ? 0 : meta.Item.Min,
                    Maximum = double.IsNaN(meta.Item.Max) ? 100 : meta.Item.Max,
                    TickFrequency = (double.IsNaN(meta.Item.Max) ? 100 : meta.Item.Max) / 100.0,
                    IsSnapToTickEnabled = false,
                    VerticalAlignment = VerticalAlignment.Center
            };

            var valueLabel = new TextBlock
            {
                    Width = 40,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
            };
            valueLabel.SetResourceReference(TextBlock.ForegroundProperty, "AppTextSecondary");

            slider.ValueChanged += (_, e) =>
            {
                valueLabel.Text = e.NewValue.ToString("F2");
                OnControlValueChanged(meta.Property);
            };

            // 用 Tag 保存对 valueLabel 的引用，方便 LoadSettingsToUi 更新
            slider.Tag = valueLabel;

            panel.Children.Add(slider);
            panel.Children.Add(valueLabel);

            return panel;
        }

#endregion

#region 值同步

        /// <summary>从 Settings 模型加载值到 UI 控件</summary>
        private void LoadSettingsToUi()
        {
            m_isLoading = true;

            foreach (var kvp in m_controlMap)
            {
                var property = kvp.Key;
                var control = kvp.Value;
                var value = property.GetValue(m_settings);

                SetControlValue(control, property, value);
            }

            m_isLoading = false;
        }

        /// <summary>将属性值设置到对应的 UI 控件</summary>
        private static void SetControlValue(FrameworkElement control, PropertyInfo property, object? value)
        {
            switch (control)
            {
                case CheckBox checkBox:
                    checkBox.IsChecked = value is true;
                    break;

                case ComboBox comboBox:
                {
                    var tagValue = value?.ToString() ?? string.Empty;
                    for (var i = 0; i < comboBox.Items.Count; i++)
                    {
                        if (comboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == tagValue)
                        {
                            comboBox.SelectedIndex = i;
                            break;
                        }
                    }

                    break;
                }

                case TextBox textBox:
                {
                    if (value is double d)
                    {
                        textBox.Text = d.ToString("F2");
                    }
                    else
                    {
                        textBox.Text = value?.ToString() ?? string.Empty;
                    }

                    break;
                }

                // Slider 被包裹在 StackPanel 中
                case StackPanel panel when panel.Children[0] is Slider slider:
                {
                    var numValue = Convert.ToDouble(value ?? 0);
                    slider.Value = numValue;

                    if (slider.Tag is TextBlock label)
                    {
                        label.Text = numValue.ToString("F2");
                    }

                    break;
                }
            }
        }

        /// <summary>控件值变更时：从控件读取值写回 Settings 模型并保存</summary>
        private void OnControlValueChanged(PropertyInfo property)
        {
            if (m_isLoading)
            {
                return;
            }

            if (!m_controlMap.TryGetValue(property, out var control))
            {
                return;
            }

            var newValue = GetControlValue(control, property);
            if (newValue == null)
            {
                return;
            }

            try
            {
                property.SetValue(m_settings, newValue);
                m_settingsProvider.Save(m_settings);
                SettingChanged?.Invoke(m_settings, property.Name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[AutoSettingsView] 保存设置项 {property.Name} 失败");
            }
        }

        /// <summary>从 UI 控件中读取当前值，转换为属性对应的类型</summary>
        private static object? GetControlValue(FrameworkElement control, PropertyInfo property)
        {
            var propType = property.PropertyType;

            switch (control)
            {
                case CheckBox checkBox:
                    return checkBox.IsChecked == true;

                case ComboBox comboBox:
                {
                    if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                    {
                        if (propType.IsEnum)
                        {
                            return Enum.Parse(propType, tag);
                        }

                        return tag;
                    }

                    return null;
                }

                case TextBox textBox:
                {
                    var text = textBox.Text;

                    if (propType == typeof(int))
                    {
                        return int.TryParse(text, out var intVal) ? intVal : null;
                    }

                    if (propType == typeof(double))
                    {
                        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal) ? doubleVal : null;
                    }

                    if (propType == typeof(float))
                    {
                        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVal) ? floatVal : null;
                    }

                    return text;
                }

                // Slider 被包裹在 StackPanel 中
                case StackPanel panel when panel.Children[0] is Slider slider:
                {
                    if (propType == typeof(int))
                    {
                        return (int)slider.Value;
                    }

                    if (propType == typeof(float))
                    {
                        return (float)slider.Value;
                    }

                    return slider.Value;
                }
            }

            return null;
        }

#endregion
    }
}
