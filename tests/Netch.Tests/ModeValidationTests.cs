using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Application;
using System.Linq;

namespace Tests;

[TestClass]
public sealed class ModeValidationTests
{
    [TestMethod]
    public void NormalizeName_TrimsVisibleName()
    {
        Assert.AreEqual("Gaming", ModeManagementService.NormalizeName("  Gaming  "));
    }

    [TestMethod]
    public void NormalizeName_RejectsControlCharacters()
    {
        var exception = Assert.ThrowsException<AppException>(() => ModeManagementService.NormalizeName("Gaming\nMode"));
        Assert.AreEqual("INVALID_MODE_NAME", exception.Code);
    }

    [TestMethod]
    public void NormalizeRules_RemovesEmptyAndDuplicateRules()
    {
        var rules = ModeManagementService.NormalizeRules(new[] { " game.exe ", "", "GAME.EXE", "^C:\\Tools" }, "handle");

        CollectionAssert.AreEqual(new[] { "game.exe", "^C:\\Tools" }, rules.ToArray());
    }

    [TestMethod]
    public void NormalizeRules_RejectsOversizedRule()
    {
        var exception = Assert.ThrowsException<AppException>(() =>
            ModeManagementService.NormalizeRules(new[] { new string('x', 1025) }, "bypass"));
        Assert.AreEqual("INVALID_MODE_RULES", exception.Code);
    }

    [TestMethod]
    public void NormalizeRules_RejectsNullEntry()
    {
        var exception = Assert.ThrowsException<AppException>(() =>
            ModeManagementService.NormalizeRules(new string[] { null! }, "handle"));
        Assert.AreEqual("INVALID_MODE_RULES", exception.Code);
    }
}
