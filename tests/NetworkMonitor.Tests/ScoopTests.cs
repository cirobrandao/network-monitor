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
}