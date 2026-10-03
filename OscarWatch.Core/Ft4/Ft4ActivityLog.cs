using System.Globalization;
using System.Text;

namespace OscarWatch.Core.Ft4;

/// <summary>Formats FT4 band activity for saving to a text file (WSJT-X ALL.txt style).</summary>
public static class Ft4ActivityLog
{
    public static string FormatLine(Ft4DecodedMessage msg)
    {
        var utc = msg.SlotUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var kind = msg.IsTransmitted ? "TX" : msg.IsOwnEcho ? "Echo" : msg.IsApriori ? "AP" : "RX";
        var snr = msg.IsTransmitted
            ? "   -"
            // The zero section stops a value such as −0.4 printing as "-+0".
            : msg.SnrDb.ToString("+0;-0;0", CultureInfo.InvariantCulture).PadLeft(4);
        var hz = msg.FreqHz.ToString("0", CultureInfo.InvariantCulture).PadLeft(5);
        return $"{utc}  {kind,-4}  {snr} dB  {hz} Hz  {msg.Text}";
    }

    public static string FormatAll(IEnumerable<Ft4DecodedMessage> messages)
    {
        // Newest-first in the UI; write oldest-first for a chronological log.
        var lines = messages
            .OrderBy(m => m.SlotUtc)
            .ThenBy(m => m.IsTransmitted ? 1 : 0)
            .Select(FormatLine);

        var sb = new StringBuilder();
        sb.AppendLine("# OscarWatch FT4 activity");
        sb.AppendLine("# UTC  Kind  SNR  Hz  Message");
        foreach (var line in lines)
            sb.AppendLine(line);
        return sb.ToString();
    }
}
