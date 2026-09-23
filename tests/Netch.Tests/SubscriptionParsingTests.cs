using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Utils;

namespace Tests;

[TestClass]
public sealed class SubscriptionParsingTests
{
    private const string TrojanLink = "trojan://test-password@example.com:443#Test";

    [TestMethod]
    public void ParseText_ParsesPlainShareLink()
    {
        var servers = ShareLink.ParseText(TrojanLink);

        Assert.AreEqual(1, servers.Count);
        Assert.AreEqual("Trojan", servers[0].Type);
    }

    [TestMethod]
    public void ParseText_ParsesBase64Subscription()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(TrojanLink));

        var servers = ShareLink.ParseText(encoded);

        Assert.AreEqual(1, servers.Count);
        Assert.AreEqual("example.com", servers[0].Hostname);
    }

    [TestMethod]
    public void ParseText_DoesNotTreatArbitraryTextAsSubscription()
    {
        var servers = ShareLink.ParseText("thisisnotasubscription");

        Assert.AreEqual(0, servers.Count);
    }
}
