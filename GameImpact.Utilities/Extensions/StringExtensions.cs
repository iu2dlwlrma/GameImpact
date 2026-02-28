namespace GameImpact.Utilities.Extensions;

/// <summary>
/// 字符串扩展方法
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// 判断字符串是否为 null 或空字符串
    /// </summary>
    /// <param name="value">字符串值</param>
    /// <returns>是否为 null 或空字符串</returns>
    public static bool IsNullOrEmpty(this string? value) => string.IsNullOrEmpty(value);

    /// <summary>
    /// 判断字符串是否为 null、空字符串或仅包含空白字符
    /// </summary>
    /// <param name="value">字符串值</param>
    /// <returns>是否为 null、空字符串或仅包含空白字符</returns>
    public static bool IsNullOrWhiteSpace(this string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// 如果字符串为 null 或空，返回默认值；否则返回原值
    /// </summary>
    /// <param name="value">字符串值</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>字符串值或默认值</returns>
    public static string OrDefault(this string? value, string defaultValue) => value.IsNullOrEmpty() ? defaultValue : value!;
}
