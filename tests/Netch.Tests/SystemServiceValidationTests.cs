using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Application;
using Netch.Models.GitHubRelease;

namespace Tests;

[TestClass]
public sealed class SystemServiceValidationTests
{
    [DataTestMethod]
    [DataRow("wintun", "wintun")]
    [DataRow(" NetFilter2 ", "netfilter2")]
    public void NormalizeDriverId_AcceptsClosedDriverSet(string input, string expected)
    {
        Assert.AreEqual(expected, DriverService.NormalizeDriverId(input));
    }

    [TestMethod]
    public void NormalizeDriverId_RejectsArbitraryServiceName()
    {
        var exception = Assert.ThrowsException<AppException>(() => DriverService.NormalizeDriverId("arbitrary-service"));
        Assert.AreEqual("INVALID_DRIVER", exception.Code);
    }

    [TestMethod]
    public void ResolvePackage_RequiresSafeHttpsAssetAndMatchingHash()
    {
        var hash = new string('a', 64);
        var release = new Release
        {
            body = $"| File | SHA256 |\n| :- | :- |\n| Netch.7z | {hash} |",
            assets = new[]
            {
                new Asset { name = "Netch.7z", size = 1024, browser_download_url = "https://example.test/Netch.7z" }
            }
        };

        var package = UpdateService.ResolvePackage(release);

        Assert.IsNotNull(package);
        Assert.AreEqual("Netch.7z", package.FileName);
        Assert.AreEqual(hash, package.Sha256);
    }

    [TestMethod]
    public void ResolvePackage_RejectsPathTraversalAsset()
    {
        var hash = new string('b', 64);
        var release = new Release
        {
            body = $"| ../Netch.7z | {hash} |",
            assets = new[]
            {
                new Asset { name = "../Netch.7z", size = 1024, browser_download_url = "https://example.test/Netch.7z" }
            }
        };

        Assert.IsNull(UpdateService.ResolvePackage(release));
    }

    [TestMethod]
    public void SelectLatestRelease_RespectsPrereleaseSetting()
    {
        var stable = new Release { tag_name = "1.9.8", draft = false, prerelease = false };
        var preview = new Release { tag_name = "2.0.0-beta1", draft = false, prerelease = true };

        Assert.AreSame(stable, UpdateService.SelectLatestRelease(new[] { preview, stable }, false));
        Assert.AreSame(preview, UpdateService.SelectLatestRelease(new[] { preview, stable }, true));
    }
}
