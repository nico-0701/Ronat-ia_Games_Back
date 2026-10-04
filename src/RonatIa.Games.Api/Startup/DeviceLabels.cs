namespace RonatIa.Games.Api.Startup;

/// <summary>Nome legível do aparelho para a lista de sessões: o informado pelo app ou um resumo do User-Agent.</summary>
public static class DeviceLabels
{
    public static string? Resolve(string? deviceName, string? userAgent)
    {
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            return deviceName.Trim();
        }

        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var os = userAgent switch
        {
            _ when userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) => "Android",
            _ when userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) => "iOS",
            _ when userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) => "Windows",
            _ when userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) => "macOS",
            _ when userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) => "Linux",
            _ => null,
        };

        var browser = userAgent switch
        {
            _ when userAgent.Contains("Edg/", StringComparison.Ordinal) => "Edge",
            _ when userAgent.Contains("OPR/", StringComparison.Ordinal) => "Opera",
            _ when userAgent.Contains("Firefox/", StringComparison.Ordinal) => "Firefox",
            _ when userAgent.Contains("Chrome/", StringComparison.Ordinal) => "Chrome",
            _ when userAgent.Contains("Safari/", StringComparison.Ordinal) => "Safari",
            _ => null,
        };

        return (os, browser) switch
        {
            ({ } o, { } b) => $"{o} · {b}",
            ({ } o, null) => o,
            (null, { } b) => b,
            _ => null,
        };
    }
}
