using OscarWatch.Recording;

namespace OscarWatch.Tests;

public sealed class PortAudioHostApiTests
{
    [Fact]
    public void GetTypeId_OffWindows_ReturnsZeroWithoutNativeLookup()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Equal(0, PortAudioHostApi.GetTypeId(0));
        Assert.Equal(0, PortAudioHostApi.GetTypeId(3));
    }
}
