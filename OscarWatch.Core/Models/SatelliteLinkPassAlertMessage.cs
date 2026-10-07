using System.Text.Json.Serialization;

namespace OscarWatch.Core.Models;

/// <summary>WebSocket broadcast when a scheduled pass enters its reminder window (protocol version 1).</summary>
public sealed class SatelliteLinkPassAlertMessage
{
    public const string MessageType = "passAlert";
    public const int ProtocolVersion = 1;

    [JsonPropertyName("type")]
    public string Type { get; init; } = MessageType;

    [JsonPropertyName("version")]
    public int Version { get; init; } = ProtocolVersion;

    [JsonPropertyName("timestampUtc")]
    public string TimestampUtc { get; init; } = "";

    [JsonPropertyName("pass")]
    public SatelliteLinkPassAlertInfo? Pass { get; init; }
}

public sealed class SatelliteLinkPassAlertInfo
{
    [JsonPropertyName("satellite")]
    public SatelliteLinkPassAlertSatelliteInfo? Satellite { get; init; }

    [JsonPropertyName("aosUtc")]
    public string AosUtc { get; init; } = "";

    [JsonPropertyName("losUtc")]
    public string LosUtc { get; init; } = "";

    [JsonPropertyName("maxElevationDeg")]
    public double MaxElevationDeg { get; init; }

    [JsonPropertyName("secondsUntilAos")]
    public int SecondsUntilAos { get; init; }
}

public sealed class SatelliteLinkPassAlertSatelliteInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("noradId")]
    public string NoradId { get; init; } = "";
}
