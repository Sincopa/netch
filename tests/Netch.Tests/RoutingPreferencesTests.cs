using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch;
using Netch.Application;
using Netch.Models;
using Netch.Models.Modes;
using Netch.Models.Modes.ProcessMode;
using Netch.Models.Modes.TunMode;
using Netch.Servers;
using Netch.Services;

namespace Tests;

[TestClass, DoNotParallelize]
public class RoutingPreferencesTests
{
    private Setting _previous = null!;
    private List<Mode> _modes = null!;
    [TestInitialize]
    public void SetUp()
    {
        _previous = Netch.Global.Settings;
        _modes = new List<Mode>(Netch.Global.Modes);
        Netch.Global.Settings = new Setting {
            Subscription = new List<Subscription> { new() { Remark = "One" }, new() { Remark = "Two" } },
            Server = new List<Server> {
                new Socks5Server { Hostname = "one.example", Port = 1080, Group = "One" },
                new Socks5Server { Hostname = "two.example", Port = 1080, Group = "One" },
                new Socks5Server { Hostname = "three.example", Port = 1080, Group = "Two" }
            }, ServerComboBoxSelectedIndex = 0
        };
        Netch.Global.Modes.Clear();
        Netch.Global.Modes.Add(new TunMode { FullName = ModeService.Instance.GetFullPath("Game.json"), Handle = new() { "203.0.113.0/24" } });
        Netch.Global.Modes.Add(new TunMode { FullName = ModeService.Instance.GetFullPath(RoutingPreferences.SystemModeFile), Handle = new() { "0.0.0.0/1", "128.0.0.0/1" } });
        Netch.Global.Modes.Add(new Redirector { FullName = ModeService.Instance.GetFullPath(Path.Combine("Custom", RoutingPreferences.ApplicationsFile())), Handle = new() { "discord\\.exe" } });
    }

    [TestCleanup]
    public void TearDown() { Netch.Global.Settings = _previous; Netch.Global.Modes.Clear(); Netch.Global.Modes.AddRange(_modes); }

    [TestMethod]
    public void WholeComputer_DoesNotSelectFirstGameTunProfile()
    {
        Assert.IsNull(RoutingPreferences.Role(Netch.Global.Modes[0]));
        Assert.AreEqual("whole-computer", RoutingPreferences.Role(Netch.Global.Modes[1]));
        Netch.Global.Settings.ModeComboBoxSelectedIndex = -1;
        Assert.AreEqual("1", new CatalogService().GetSelectedModeId());
    }

    [TestMethod]
    public void ApplicationSelection_SurvivesServerChangeRenameAndSerialization()
    {
        Netch.Global.Settings.ModeComboBoxSelectedIndex = 2;
        RoutingPreferences.RememberSelection();
        var file = RoutingPreferences.ApplicationsFile();
        Netch.Global.Settings.ServerComboBoxSelectedIndex = 1;
        RoutingPreferences.RestoreSelection();
        Assert.AreEqual(2, Netch.Global.Settings.ModeComboBoxSelectedIndex);
        Assert.AreEqual(file, RoutingPreferences.ApplicationsFile());
        var subscription = JsonSerializer.Deserialize<Subscription>(JsonSerializer.Serialize(Netch.Global.Settings.Subscription[0]))!;
        subscription.Remark = "Renamed";
        Netch.Global.Settings.Subscription[0] = subscription;
        Netch.Global.Settings.Server[1].Group = "Renamed";
        Netch.Global.Modes.Reverse();
        RoutingPreferences.RestoreSelection();
        Assert.AreEqual("selected-applications", RoutingPreferences.Role(Netch.Global.Modes[Netch.Global.Settings.ModeComboBoxSelectedIndex]));
        Assert.AreEqual(file, RoutingPreferences.ApplicationsFile());
    }

    [TestMethod]
    public void DifferentSubscription_DoesNotReuseAnotherSubscriptionsApplications()
    {
        Netch.Global.Settings.ModeComboBoxSelectedIndex = 2;
        RoutingPreferences.RememberSelection();
        var first = RoutingPreferences.ApplicationsFile();
        Netch.Global.Settings.ServerComboBoxSelectedIndex = 2;
        RoutingPreferences.RestoreSelection();
        Assert.AreNotEqual(first, RoutingPreferences.ApplicationsFile());
        Assert.AreEqual(1, Netch.Global.Settings.ModeComboBoxSelectedIndex);
        Assert.IsNull(RoutingPreferences.ApplicationsMode());
    }

    [TestMethod]
    public void WholeComputer_KeepsPreviouslyChosenApplications()
    {
        Netch.Global.Settings.ModeComboBoxSelectedIndex = 1;
        RoutingPreferences.RememberSelection();
        using var connection = new ConnectionService(new CatalogService());
        var routing = new ProcessService(connection).GetRouting();
        Assert.AreEqual("all", routing.Mode);
        CollectionAssert.AreEqual(new[] { "discord.exe" }, new List<string>(routing.Processes));
    }

    [TestMethod]
    public void LegacyApplications_RemainAvailableAfterSwitchingToWholeComputer()
    {
        Netch.Global.Modes.RemoveAt(2);
        Netch.Global.Modes.Add(new Redirector { FullName = ModeService.Instance.GetFullPath(Path.Combine("Custom", RoutingPreferences.LegacyApplicationsFile)), Handle = new() { "steam\\.exe" } });
        Netch.Global.Settings.ModeComboBoxSelectedIndex = 1;
        RoutingPreferences.RememberSelection();
        using var connection = new ConnectionService(new CatalogService());
        CollectionAssert.AreEqual(new[] { "steam.exe" }, new List<string>(new ProcessService(connection).GetRouting().Processes));
    }
}
