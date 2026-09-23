using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Servers;
using Netch.Utils;

namespace Tests;

[TestClass]
public class TunConfigTests
{
    [TestMethod]
    public void TunEgress_BindsAllBalancerCandidatesAndPreservesTransport()
    {
        var config = JsonNode.Parse("""
            {"outbounds":[
              {"protocol":"vless","streamSettings":{"network":"xhttp","sockopt":{"tcpFastOpen":true}}},
              {"protocol":"vless"},{"protocol":"freedom"},{"protocol":"blackhole"}],
             "routing":{"balancers":[{"tag":"auto","selector":["proxy"]}]}}
            """)!.AsObject();
        XrayProfileConfig.BindOutboundInterface(config, "Ethernet");
        foreach (var outbound in config["outbounds"]!.AsArray())
            if (outbound!["protocol"]!.GetValue<string>() != "blackhole")
                Assert.AreEqual("Ethernet", outbound["streamSettings"]!["sockopt"]!["interface"]!.GetValue<string>());
        Assert.AreEqual("xhttp", config["outbounds"]![0]!["streamSettings"]!["network"]!.GetValue<string>());
        Assert.IsTrue(config["outbounds"]![0]!["streamSettings"]!["sockopt"]!["tcpFastOpen"]!.GetValue<bool>());
        Assert.IsNull(config["outbounds"]![3]!["streamSettings"]);
        Assert.AreEqual("auto", config["routing"]!["balancers"]![0]!["tag"]!.GetValue<string>());
    }

    [TestMethod]
    public void ProcessMode_DoesNotAddAnInterfaceBinding()
    {
        var config = JsonNode.Parse("""{"outbounds":[{"protocol":"vless"}]}""")!.AsObject();
        var original = config.ToJsonString();
        XrayProfileConfig.BindOutboundInterface(config, null);
        Assert.AreEqual(original, config.ToJsonString());
    }

    [DataTestMethod]
    [DataRow("0.0.0.0/no")]
    [DataRow("0.0.0.0/33")]
    [DataRow("0.0.0.0/-1")]
    [DataRow("example.org/24")]
    [DataRow("::/0")]
    public void InvalidRoute_IsRejectedWithoutThrowing(string rule)
    {
        Assert.IsFalse(RouteUtils.TryParseIPNetwork(rule, out _, out _));
    }
}
