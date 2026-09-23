using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Application;

namespace Tests;

[TestClass]
public sealed class ServerIdentityTests
{
    [TestMethod]
    public void CreateFavoriteKey_IsNormalizedAndOpaque()
    {
        var first = ServerIdentity.CreateFavoriteKey("VLESS", " Example.COM. ", 443);
        var second = ServerIdentity.CreateFavoriteKey("vless", "example.com", 443);

        Assert.AreEqual(first, second);
        StringAssert.StartsWith(first, "v1:");
        Assert.IsFalse(first.Contains("example.com", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(first.Contains("vless", StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow("Germany, Frankfurt", "NONE", "edge.example.com", "DE")]
    [DataRow("Server", "Netherlands", "edge.example.com", "NL")]
    [DataRow("🇺🇸 New York", "NONE", "edge.example.com", "US")]
    [DataRow("Server", "NONE", "edge.example.jp", "JP")]
    [DataRow("Server", "NONE", "192.0.2.1", null)]
    public void ResolveCountryCode_UsesLocalHints(string name, string group, string hostname, string expected)
    {
        Assert.AreEqual(expected, ServerIdentity.ResolveCountryCode(name, group, hostname));
    }
}
