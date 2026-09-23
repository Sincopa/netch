using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Application;

namespace Tests;

[TestClass]
public sealed class SettingsValidationTests
{
    [TestMethod]
    public void Validate_AcceptsValidSettings()
    {
        SettingsService.Validate(CreateValid());
    }

    [TestMethod]
    public void Validate_RejectsConflictingLocalPorts()
    {
        var value = CreateValid();
        value = value with { Connection = value.Connection with { HttpPort = value.Connection.Socks5Port } };

        var exception = Assert.ThrowsException<AppException>(() => SettingsService.Validate(value));

        Assert.AreEqual("INVALID_SETTINGS", exception.Code);
    }

    [TestMethod]
    public void Validate_AcceptsHttpLatencyUrlAndRejectsInvalidScheme()
    {
        var value = CreateValid();
        value = value with { Connection = value.Connection with { PingMethod = "http", LatencyTestUrl = "https://www.google.com/generate_204" } };
        SettingsService.Validate(value);
        value = value with { Connection = value.Connection with { LatencyTestUrl = "file:///test" } };
        Assert.ThrowsException<AppException>(() => SettingsService.Validate(value));
    }

    [TestMethod]
    public void Validate_RejectsInvalidTunAddress()
    {
        var value = CreateValid();
        value = value with { Dns = value.Dns with { TunAddress = "not-an-ip" } };

        Assert.ThrowsException<AppException>(() => SettingsService.Validate(value));
    }

    [TestMethod]
    public void Validate_AcceptsLegacyDnsHostAndPort()
    {
        var value = CreateValid();
        value = value with { Dns = value.Dns with { ChinaDns = "223.5.5.5:53", OtherDns = "1.1.1.1:53" } };

        SettingsService.Validate(value);
    }

    [TestMethod]
    public void Validate_RejectsIncompleteDocument()
    {
        var value = CreateValid() with { General = null! };

        var exception = Assert.ThrowsException<AppException>(() => SettingsService.Validate(value));
        Assert.AreEqual("INVALID_SETTINGS", exception.Code);
    }

    private static SettingsDto CreateValid() => new(
        new GeneralSettingsDto("System", 4, 5),
        new ConnectionSettingsDto(2801, 2802, "127.0.0.1", "tcp", 10000, 10, -1, "stun.syncthing.net", 3478),
        new RoutingSettingsDto(true, true, false, 10, true, false, true, true, "1.1.1.1:53"),
        new SubscriptionSettingsDto(false),
        new DnsSettingsDto(
            "tcp://223.5.5.5:53",
            "tcp://1.1.1.1:53",
            "10.0.236.10",
            "255.255.255.0",
            "10.0.236.1",
            false,
            "1.1.1.1",
            false,
            new[] { "192.168.0.0/16" }),
        new StartupSettingsDto(false, false, false, true, false),
        new UpdateSettingsDto(true, false),
        new AdvancedSettingsDto(true, false, false, false, false, 1350, 50, 12, 100, 2, 2, false));
}
