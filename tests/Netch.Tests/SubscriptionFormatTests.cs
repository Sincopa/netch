using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Application;
using Netch.Models;
using Netch.Servers;
using Netch.Utils;

namespace Tests;

[TestClass]
[DoNotParallelize]
public sealed class SubscriptionFormatTests
{
    private const string Xray = """
        [{"remarks":"Example","routing":{"rules":[]},"inbounds":[],"outbounds":[
          {"protocol":"vless","settings":{"vnext":[{"address":"example.com","port":443,"users":[{"id":"11111111-1111-4111-8111-111111111111","encryption":"none","flow":"xtls-rprx-vision"}]}]},
           "streamSettings":{"network":"tcp","security":"reality","realitySettings":{"serverName":"sni.example.com","fingerprint":"chrome","publicKey":"test-public-key","shortId":"abcd"}}},
          {"protocol":"freedom"},{"protocol":"blackhole"}]}]
        """;

    [TestMethod]
    public void XrayJson_ImportsVlessRealityInsteadOfEmptyShadowsocks()
    {
        var servers = ShareLink.ParseText(Xray);
        Assert.AreEqual(1, servers.Count);
        var server = (VLESSServer)servers.Single();
        Assert.AreEqual("example.com", server.Hostname);
        Assert.AreEqual((ushort)443, server.Port);
        Assert.AreEqual("reality", server.TLSSecureType);
        Assert.AreEqual("xtls-rprx-vision", server.Flow);
        Assert.AreEqual("test-public-key", server.RealityPublicKey);
        Assert.AreEqual("abcd", server.RealityShortId);
        Assert.AreEqual("sni.example.com", server.ServerName);
        var restored = (VLESSServer)ShareLink.ParseText(V2rayUtils.GetVShareLink(server, "vless")).Single();
        Assert.AreEqual(server.Flow, restored.Flow);
        Assert.AreEqual(server.RealityPublicKey, restored.RealityPublicKey);
    }

    [TestMethod]
    public void JsonWithUnknownSchema_DoesNotCreateServers()
    {
        Assert.AreEqual(0, ShareLink.ParseText("[{\"dns\":{},\"remarks\":\"Not a server\"}]").Count);
        Assert.AreEqual(0, ShareLink.ParseText("[{\"server\":null,\"server_port\":443,\"method\":\"aes-128-gcm\"}]").Count);
        Assert.AreEqual(0, ShareLink.ParseText("null").Count);
    }

    [TestMethod]
    public async System.Threading.Tasks.Task AutomaticProfile_PreservesBalancerAndOutboundsInOneEntryAsync()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(Xray)!;
        var profile = json[0]!;
        var outbounds = profile["outbounds"]!.AsArray();
        outbounds[0]!["tag"] = "proxy-1";
        var second = outbounds[0]!.DeepClone();
        second["tag"] = "proxy-2";
        outbounds.Add(second);
        profile["routing"] = System.Text.Json.Nodes.JsonNode.Parse("""{"rules":[{"type":"field","inboundTag":["socks"],"network":"tcp,udp","balancerTag":"auto"}],"balancers":[{"tag":"auto","selector":["proxy-"],"strategy":{"type":"leastPing"}}]}""");
        profile["inbounds"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"tag":"socks","port":9999,"listen":"0.0.0.0","protocol":"socks"}]""");
        profile["observatory"] = System.Text.Json.Nodes.JsonNode.Parse("""{"subjectSelector":["proxy-"],"probeURL":"https://example.org/204","probeInterval":"30s"}""");
        var server = ShareLink.ParseText(json.ToJsonString()).Single();
        Assert.IsTrue(server.IsAutomatic);
        var config = await XrayProfileConfig.GenerateAsync(server, 12345, "127.0.0.1");
        Assert.AreEqual(4, config["outbounds"]!.AsArray().Count);
        Assert.AreEqual("leastPing", config["routing"]!["balancers"]![0]!["strategy"]!["type"]!.GetValue<string>());
        Assert.AreEqual(1, config["inbounds"]!.AsArray().Count);
        Assert.AreEqual((ushort)12345, config["inbounds"]![0]!["port"]!.GetValue<ushort>());
        Assert.AreEqual("127.0.0.1", config["inbounds"]![0]!["listen"]!.GetValue<string>());
        Assert.AreEqual("netch-socks", config["routing"]!["rules"]![0]!["inboundTag"]![0]!.GetValue<string>());
        Assert.AreEqual(Netch.Global.Settings.LatencyTestUrl, config["observatory"]!["probeURL"]!.GetValue<string>());
        Assert.AreEqual(9999, server.XrayProfile!.Value.GetProperty("inbounds")[0].GetProperty("port").GetInt32());
    }

    [TestMethod]
    public void ShadowsocksJson_StillImportsValidEntries()
    {
        var servers = ShareLink.ParseText("[{\"server\":\"example.com\",\"server_port\":443,\"method\":\"aes-128-gcm\",\"password\":\"test\"}]");
        Assert.AreEqual(1, servers.Count);
        Assert.IsInstanceOfType(servers.Single(), typeof(ShadowsocksServer));
    }

    [TestMethod]
    public void Catalog_SkipsOldInvalidImportsWithoutChangingServerIds()
    {
        var previous = Netch.Global.Settings;
        try
        {
            Netch.Global.Settings = new Setting();
            Netch.Global.Settings.Server.Add(new ShadowsocksServer { Hostname = null!, Port = 0 });
            Netch.Global.Settings.Server.Add(new VLESSServer { Hostname = "example.com", Port = 443 });
            Netch.Global.Settings.ServerComboBoxSelectedIndex = 0;
            var catalog = new CatalogService();
            var valid = catalog.GetServers().Single();
            Assert.AreEqual("1", valid.Id);
            Assert.AreEqual("1", catalog.GetSelectedServerId());
            Assert.AreSame(Netch.Global.Settings.Server[1], catalog.ResolveServer(valid.Id));
            Assert.ThrowsException<AppException>(() => catalog.ResolveServer("0"));
            Assert.AreEqual(2, Netch.Global.Settings.Server.Count);
        }
        finally { Netch.Global.Settings = previous; }
    }

    [TestMethod]
    public void RussianLocale_IsAvailableAndTranslatesNativeUi()
    {
        var previous = i18N.LangCode;
        try
        {
            i18N.Load("ru-RU");
            Assert.AreEqual("Подключиться", i18N.Translate("Connect"));
            Assert.AreEqual("Открыть Netch", i18N.Translate("Open Netch"));
            CollectionAssert.Contains(i18N.GetTranslateList(), "ru-RU");
        }
        finally { i18N.Load(previous); }
    }
}
