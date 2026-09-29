namespace OscarWatch.Core.Radio;

/// <summary>Downlink/uplink mode strings for the transponder database and CAT drivers.</summary>
public static class TransponderCatModes
{
    public static readonly IReadOnlyList<string> EditorOptions =
    [
        "USB",
        "LSB",
        "FM",
        "FMN",
        "CW",
        "DATA-USB",
        "DATA-LSB",
        "DATA-FM"
    ];

    /// <summary>Canonical form for storage and CAT (e.g. FM-DATA → DATA-FM).</summary>
    public static string Normalize(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return "";

        return mode.Trim().ToUpperInvariant() switch
        {
            "FM-DATA" => "DATA-FM",
            _ => mode.Trim().ToUpperInvariant()
        };
    }

    public static bool IsDigitalFm(string mode) =>
        Normalize(mode) == "DATA-FM";

    /// <summary>
    /// Catalogue data mode (DATA-USB, DATA-LSB, DATA-FM). FM-DATA is treated as DATA-FM.
    /// </summary>
    public static bool IsData(string? mode)
    {
        var normalized = Normalize(mode ?? "");
        return normalized is "DATA-USB" or "DATA-LSB" or "DATA-FM";
    }
}
