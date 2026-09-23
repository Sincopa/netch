using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Application;
using System.Linq;

namespace Tests;

[TestClass]
public sealed class ProcessValidationTests
{
    [TestMethod]
    public void NormalizeProcesses_UsesExecutableNamesAndDeduplicates()
    {
        var values = ProcessService.NormalizeProcesses(new[] { @"C:\Games\game.exe", "GAME.EXE", "discord" });

        CollectionAssert.AreEqual(new[] { "game.exe", "discord.exe" }, values);
    }

    [TestMethod]
    public void NormalizeProcesses_RejectsOversizedPayload()
    {
        var values = Enumerable.Repeat("app.exe", 513).ToArray();

        var exception = Assert.ThrowsException<AppException>(() => ProcessService.NormalizeProcesses(values));
        Assert.AreEqual("INVALID_PROCESS_SELECTION", exception.Code);
    }

    [TestMethod]
    public void NormalizeProcesses_RejectsMoreThan128UniqueApplications()
    {
        var values = Enumerable.Range(0, 129).Select(index => $"app-{index}.exe").ToArray();

        var exception = Assert.ThrowsException<AppException>(() => ProcessService.NormalizeProcesses(values));
        Assert.AreEqual("INVALID_PROCESS_SELECTION", exception.Code);
    }
}
