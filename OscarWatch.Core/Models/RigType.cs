namespace OscarWatch.Core.Models;

public enum RigType
{
    None,
    IcomIc910,
    IcomIc9100,
    IcomIc9700,
    IcomIc821h,
    IcomIc705,
    IcomIc7300,
    IcomIc905,
    IcomIc7100,
    IcomIc706,
    IcomIc706Mkii,
    IcomIc706MkiiG,
    YaesuFt847,
    YaesuFt817,
    YaesuFt818,
    YaesuFt991,
    YaesuFt991a,
    YaesuFtx1,
    KenwoodTs2000,
    KenwoodThD74,
    KenwoodThD75,
    /// <summary>SDR application rigctl TCP server (dual-radio downlink only).</summary>
    SdrRigCtlTcp,
    /// <summary>FlexRadio SmartSDR TCP/IP API (single-radio full duplex).</summary>
    FlexSmartSdr,
    Dummy,
    /// <summary>Yaesu FT-857 / FT-857D (dual-radio leg; FT-817 five-byte CAT).</summary>
    YaesuFt857
}
