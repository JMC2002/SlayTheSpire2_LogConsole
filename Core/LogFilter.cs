using System.Text.RegularExpressions;

namespace JmcLogConsole.Core;

public sealed class LogFilter
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100.0);
    private readonly Regex? regex;

    private LogFilter(string pattern, Regex? regex, string? error)
    {
        Pattern = pattern;
        this.regex = regex;
        Error = error;
    }

    public string Pattern { get; }
    public string? Error { get; private set; }
    public bool HasPattern => !string.IsNullOrWhiteSpace(Pattern);
    public bool IsUsable => !HasPattern || (regex != null && string.IsNullOrWhiteSpace(Error));

    public static LogFilter Create(string? pattern)
    {
        string normalized = pattern ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new LogFilter(string.Empty, null, null);
        }

        try
        {
            var compiledRegex = new Regex(
                normalized,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
            return new LogFilter(normalized, compiledRegex, null);
        }
        catch (ArgumentException ex)
        {
            return new LogFilter(normalized, null, "正则无效：" + ex.Message);
        }
    }

    public bool Matches(string value)
    {
        if (!HasPattern)
        {
            return true;
        }

        if (regex == null)
        {
            return false;
        }

        try
        {
            return regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            Error = "正则筛选超时。";
            return false;
        }
    }
}
