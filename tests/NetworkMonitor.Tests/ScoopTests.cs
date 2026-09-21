using NetworkMonitor;
using NetworkMonitor.Services;
using Xunit;

public class ScoopTests
{
    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("10.0.0.1", true)]
    [InlineData("192.168.0.1", true)]
    [InlineData("127.0.0.1", true)]
    public void Geo_NonPublic(string ip, bool expected)
        => Assert.Equal(expected, GeoIpService.IsNonPublic(ip));

    [Fact]
    public void Format_Rate_Works()
        => Assert.False(string.IsNullOrWhiteSpace(Format.Rate(2048)));

    [Fact]
    public void AlertRule_Fields()
    {
        var rule = new AlertRule { ProcessNameContains = "chrome", MaxDownMBps = 2, MaxUpMBps = 1, Enabled = true };
        Assert.True(rule.Enabled);
    }

    [Fact]
    public void FlagEmoji_FromCountryCode()
    {
        Assert.Equal(Regional("BR"), Format.FlagEmoji("BR"));
        Assert.Equal(Regional("US"), Format.FlagEmoji("us"));
        Assert.Equal(Regional("DE"), Format.FlagEmoji("DE"));
    }

    private static string Regional(string code)
        => string.Concat(
            char.ConvertFromUtf32(0x1F1E6 + (char.ToUpperInvariant(code[0]) - 'A')),
            char.ConvertFromUtf32(0x1F1E6 + (char.ToUpperInvariant(code[1]) - 'A')));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("USA")]
    public void FlagEmoji_Invalid_Empty(string? code)
        => Assert.Equal("", Format.FlagEmoji(code));

    [Fact]
    public void Theme_ExplicitModes()
    {
        Assert.True(Theme.ResolveLight("Light"));
        Assert.False(Theme.ResolveLight("Dark"));
    }
}