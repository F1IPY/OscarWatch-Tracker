using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4DecodeDepthTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(19.9)]
    public void Low_elevation_uses_deep_decode(double elevationDeg)
    {
        Assert.True(Ft4DecodeDepth.UseDeep(elevationDeg));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(45)]
    [InlineData(90)]
    public void Mid_pass_stays_on_the_fast_budget(double elevationDeg)
    {
        Assert.False(Ft4DecodeDepth.UseDeep(elevationDeg));
    }

    [Fact]
    public void Below_the_horizon_stays_fast()
    {
        Assert.False(Ft4DecodeDepth.UseDeep(-2));
    }

    [Fact]
    public void Missing_elevation_stays_fast()
    {
        Assert.False(Ft4DecodeDepth.UseDeep(null));
    }
}
