namespace GameImpact.Utilities.Extensions;

public static class StringExtensions
{
    public static bool IsNullOrEmpty(this string? value) => string.IsNullOrEmpty(value);
    public static bool IsNullOrWhiteSpace(this string? value) => string.IsNullOrWhiteSpace(value);
    public static string OrDefault(this string? value, string defaultValue) => value.IsNullOrEmpty() ? defaultValue : value!;
}
