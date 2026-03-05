namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Reporting;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

static class ConsoleLog
{
    static readonly long StartTimestamp = Stopwatch.GetTimestamp();
    static bool atLineStart = true;

    internal static bool ColorEnabled { get; } =
        Environment.GetEnvironmentVariable("NO_COLOR") is null
        && !Console.IsOutputRedirected;

    public static void Write(string text)
    {
        WritePrefix();
        Console.Write(text);
    }

    public static void Write(ref ConsoleLogInterpolatedStringHandler handler)
    {
        WritePrefix();
        Console.Write(handler.ToStringAndClear());
    }

    public static void WriteLine()
    {
        WritePrefix();
        Console.WriteLine();
        atLineStart = true;
    }

    public static void WriteLine(string text)
    {
        WritePrefix();
        Console.WriteLine(text);
        atLineStart = true;
    }

    public static void WriteLine(ref ConsoleLogInterpolatedStringHandler handler)
    {
        WritePrefix();
        Console.WriteLine(handler.ToStringAndClear());
        atLineStart = true;
    }

    static void WritePrefix()
    {
        if (!atLineStart)
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(StartTimestamp);
        var minutes = (int)elapsed.TotalMinutes;

        if (ColorEnabled)
        {
            Console.Write($"\x1b[90m[{minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds:D3}]\x1b[0m ");
        }
        else
        {
            Console.Write($"[{minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds:D3}] ");
        }

        atLineStart = false;
    }

    internal static string Dim(string text) => ColorEnabled ? $"\x1b[90m{text}\x1b[0m" : text;
    internal static string Bold(string text) => ColorEnabled ? $"\x1b[1m{text}\x1b[0m" : text;
    internal static string Yellow(string text) => ColorEnabled ? $"\x1b[33m{text}\x1b[0m" : text;
    internal static string Red(string text) => ColorEnabled ? $"\x1b[1;31m{text}\x1b[0m" : text;
}

[InterpolatedStringHandler]
ref struct ConsoleLogInterpolatedStringHandler
{
    StringBuilder builder;
    readonly bool colorEnabled;

    public ConsoleLogInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        builder = new StringBuilder(literalLength + (formattedCount * 24));
        colorEnabled = ConsoleLog.ColorEnabled;
    }

    public void AppendLiteral(string s) => builder.Append(s);

    public void AppendFormatted<T>(T value)
    {
        Append(value?.ToString() ?? "", ColorFor<T>());
    }

    public void AppendFormatted<T>(T value, string? format)
    {
        Append(value is IFormattable f ? f.ToString(format, null) : value?.ToString() ?? "", ColorFor<T>());
    }

    public void AppendFormatted<T>(T value, int alignment)
    {
        Append(Align(value?.ToString() ?? "", alignment), ColorFor<T>());
    }

    public void AppendFormatted<T>(T value, int alignment, string? format)
    {
        var formatted = value is IFormattable f ? f.ToString(format, null) : value?.ToString() ?? "";
        Append(Align(formatted, alignment), ColorFor<T>());
    }

    void Append(string formatted, string ansiColor)
    {
        if (colorEnabled)
        {
            builder.Append(ansiColor);
            builder.Append(formatted);
            builder.Append("\x1b[0m");
        }
        else
        {
            builder.Append(formatted);
        }
    }

    static string ColorFor<T>()
    {
        var t = typeof(T);

        if (t == typeof(int) || t == typeof(long) || t == typeof(double)
            || t == typeof(float) || t == typeof(decimal)
            || t == typeof(short) || t == typeof(byte))
        {
            return "\x1b[36m"; // Cyan — numeric values
        }

        if (t == typeof(string))
        {
            return "\x1b[33m"; // Yellow — strings/identifiers
        }

        if (t.IsEnum)
        {
            return "\x1b[32m"; // Green — enums/status
        }

        if (t == typeof(TimeSpan) || t == typeof(DateTime) || t == typeof(DateTimeOffset))
        {
            return "\x1b[35m"; // Magenta — time values
        }

        if (t == typeof(bool))
        {
            return "\x1b[32m"; // Green — booleans
        }

        return "\x1b[36m"; // Cyan — fallback
    }

    static string Align(string value, int alignment) =>
        alignment switch
        {
            > 0 => value.PadLeft(alignment),
            < 0 => value.PadRight(-alignment),
            _ => value
        };

    internal string ToStringAndClear()
    {
        var result = builder.ToString();
        builder.Clear();
        return result;
    }
}
