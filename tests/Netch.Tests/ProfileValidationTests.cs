using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Application;
using Netch.Models;
using Netch.Models.Modes.TunMode;
using Netch.Servers;
using System.Collections.Generic;
using System.Linq;

namespace Tests;

[TestClass]
public sealed class ProfileValidationTests
{
    [TestMethod]
    public void ValidateSlot_AcceptsConfiguredSlot()
    {
        ProfileService.ValidateSlot(3, 4);
    }

    [DataTestMethod]
    [DataRow(-1, 4)]
    [DataRow(4, 4)]
    [DataRow(0, 0)]
    [DataRow(0, 101)]
    public void ValidateSlot_RejectsOutsideConfiguredRange(int slot, int count)
    {
        var exception = Assert.ThrowsException<AppException>(() => ProfileService.ValidateSlot(slot, count));

        Assert.AreEqual("INVALID_PROFILE_SLOT", exception.Code);
    }

    [TestMethod]
    public void NormalizeName_TrimsValue()
    {
        Assert.AreEqual("Gaming", ProfileService.NormalizeName("  Gaming  "));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("Bad\nName")]
    public void NormalizeName_RejectsInvalidValue(string value)
    {
        Assert.ThrowsException<AppException>(() => ProfileService.NormalizeName(value));
    }

    [TestMethod]
    public void GetAll_ReportsReadyAndMissingReferencesWithoutRemovingThem()
    {
        var previousSettings = Netch.Global.Settings;
        var previousModes = Netch.Global.Modes.ToList();
        try
        {
            var server = new Socks5Server("127.0.0.1", 1080) { Remark = "Local test" };
            var mode = new TunMode { Remark = new Dictionary<string, string> { ["en"] = "Full tunnel" } };
            Netch.Global.Settings = new Setting
            {
                ProfileCount = 2,
                Server = new List<Server> { server },
                Profiles = new List<Profile>
                {
                    new(server, mode, "Ready", 0),
                    new() { Index = 1, ProfileName = "Missing", ServerRemark = "Removed", ModeRemark = "Removed" }
                }
            };
            Netch.Global.Modes.Clear();
            Netch.Global.Modes.Add(mode);

            var profiles = new ProfileService(new CatalogService()).GetAll();

            Assert.AreEqual("ready", profiles[0].Status);
            Assert.AreEqual("0", profiles[0].ServerId);
            Assert.AreEqual("0", profiles[0].ModeId);
            Assert.AreEqual("missing", profiles[1].Status);
            Assert.AreEqual(2, Netch.Global.Settings.Profiles.Count);
        }
        finally
        {
            Netch.Global.Settings = previousSettings;
            Netch.Global.Modes.Clear();
            Netch.Global.Modes.AddRange(previousModes);
        }
    }
}
