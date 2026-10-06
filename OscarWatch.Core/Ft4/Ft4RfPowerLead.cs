namespace OscarWatch.Core.Ft4;

/// <summary>
/// The TX RF power check is a CAT round trip. Starting it shortly before the slot keeps it
/// off the boundary, while the window for an unchecked power change stays a fraction of a second.
/// </summary>
public static class Ft4RfPowerLead
{
    public static readonly TimeSpan Lead = TimeSpan.FromMilliseconds(300);

    /// <summary>When to start the RF power read for a TX slot opening at <paramref name="slotStartUtc"/>.</summary>
    public static DateTime CheckAtUtc(DateTime slotStartUtc) => slotStartUtc - Lead;

    /// <summary>Block until the read has answered. TX must not start before it does.</summary>
    public static bool AwaitVerdict(Task<bool> check) => check.GetAwaiter().GetResult();
}
