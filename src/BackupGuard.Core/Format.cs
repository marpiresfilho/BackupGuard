using System.Globalization;
using System.Net;

namespace BackupGuard.Core;

public static class Format
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : value.ToString("0.##", PtBr) + " " + Units[unit];
    }

    public static string Duration(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours}h{span.Minutes:00}min"
        : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}min{span.Seconds:00}s" : $"{span.TotalSeconds:0}s";

    public static string Percent(double fraction) => fraction.ToString("0.#%", PtBr);

    public static string Local(DateTimeOffset utc) => utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", PtBr);

    /// <summary>Escapa texto para o modo HTML do Telegram.</summary>
    public static string Html(string? text) => WebUtility.HtmlEncode(text ?? "");
}
