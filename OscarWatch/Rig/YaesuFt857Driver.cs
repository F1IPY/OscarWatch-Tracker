using OscarWatch.Core.Models;

namespace OscarWatch.Rig;

/// <summary>Yaesu FT-857 / FT-857D: same five-byte CAT protocol as FT-817.</summary>
public sealed class YaesuFt857Driver : YaesuFt817Driver
{
    public YaesuFt857Driver(string port, int baudRate, RigRegion region = RigRegion.EU, int catDelayMs = 50)
        : base(RigType.YaesuFt857, port, baudRate, region, catDelayMs)
    {
    }

    internal YaesuFt857Driver(IYaesuCatTransport transport, RigRegion region = RigRegion.EU, int catDelayMs = 50)
        : base(RigType.YaesuFt857, transport, region, catDelayMs)
    {
    }
}
