using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Netch;
using Netch.Application;
using Netch.Models;
using Netch.Servers;
using System.Text.Json;

namespace Tests;

[TestClass]
[DoNotParallelize]
public sealed class ServerValidationTests
{
    [TestMethod]
    public void NormalizeName_TrimsVisibleName()
    {
        Assert.AreEqual("Frankfurt", ServerService.NormalizeName("  Frankfurt  "));
    }

    [TestMethod]
    public void NormalizeGroup_UsesDefaultForBlankValue()
    {
        Assert.AreEqual(Constants.DefaultGroup, ServerService.NormalizeGroup(" ", new[] { "Work" }));
        Assert.AreEqual(Constants.DefaultGroup, ServerService.NormalizeGroup("none", new[] { "Work" }));
    }

    [TestMethod]
    public void NormalizeGroup_RejectsSubscriptionCollision()
    {
        var exception = Assert.ThrowsException<AppException>(() =>
            ServerService.NormalizeGroup("work", new[] { "Work" }));

        Assert.AreEqual("SERVER_GROUP_RESERVED", exception.Code);
    }

    [DataTestMethod]
    [DataRow(0, 0, 0, -1)]
    [DataRow(2, 1, 3, 1)]
    [DataRow(1, 1, 3, 1)]
    [DataRow(0, 2, 3, 0)]
    [DataRow(-1, 0, 3, 0)]
    public void AdjustSelectedIndex_PreservesValidSelection(int selected, int removed, int remaining, int expected)
    {
        Assert.AreEqual(expected, ServerService.AdjustSelectedIndex(selected, removed, remaining));
    }

    [DataTestMethod]
    [DataRow("SS")]
    [DataRow("SSR")]
    [DataRow("SOCKS")]
    [DataRow("Trojan")]
    [DataRow("VMess")]
    [DataRow("VLESS")]
    [DataRow("SSH")]
    [DataRow("WireGuard")]
    public void NormalizeProtocol_AcceptsSupportedProtocol(string protocol)
    {
        Assert.AreEqual(protocol, ServerService.NormalizeProtocol(protocol));
    }

    [TestMethod]
    public void EditorDocument_DoesNotReturnServerSecrets()
    {
        var previous = Netch.Global.Settings;
        try
        {
            Netch.Global.Settings = new Setting
            {
                Server = new List<Server>
                {
                    new ShadowsocksServer
                    {
                        Remark = "Secret test", Group = Netch.Constants.DefaultGroup,
                        Hostname = "example.test", Port = 443, Password = "do-not-expose-this-value"
                    }
                }
            };
            using var connection = new ConnectionService(new CatalogService());
            var document = new ServerService(new CatalogService(), connection).GetEditorDocument();
            var json = JsonSerializer.Serialize(document);

            Assert.IsFalse(json.Contains("do-not-expose-this-value", StringComparison.Ordinal));
            Assert.IsTrue(document.Servers[0].SecretConfigured["password"]);
        }
        finally
        {
            Netch.Global.Settings = previous;
        }
    }

    [TestMethod]
    public void ValidateProtocolFields_RejectsFieldsFromAnotherProtocol()
    {
        var request = new ServerDetailsWriteRequest(
            null, "SS", "Test", Constants.DefaultGroup, "example.test", 443,
            new Dictionary<string, string> { ["privateKey"] = "not-valid-for-ss" },
            new Dictionary<string, string> { ["password"] = "secret" },
            Array.Empty<string>());

        var exception = Assert.ThrowsException<AppException>(() => ServerService.ValidateProtocolFields("SS", request));
        Assert.AreEqual("INVALID_SERVER_FIELD", exception.Code);
    }
}
