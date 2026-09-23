using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Controllers;

namespace Tests;

[TestClass, DoNotParallelize]
public sealed class GuardLifecycleTests
{
    private sealed class OutputGuard : Guard
    {
        public OutputGuard(string file) : base(file) { }
        public override string Name => "GuardLifecycleTest";
        public Task StartAsync() => StartGuardAsync("/d /c \"@for /l %i in (1,1,4000) do @(echo stdout-%i & echo stderr-%i 1>&2)\"");
    }

    [TestMethod]
    public async Task Stop_DrainsConcurrentOutputAndCanBeCalledTwiceAsync()
    {
        var bin = Path.Combine(Netch.Global.NetchDir, "bin");
        var logging = Path.Combine(Netch.Global.NetchDir, "logging");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(logging);
        var executable = "guard-test-" + Guid.NewGuid().ToString("N") + ".exe";
        var target = Path.Combine(bin, executable);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), target);
        try
        {
            var guard = new OutputGuard(executable);
            await guard.StartAsync();
            await Task.Delay(75);
            await guard.StopAsync();
            await guard.StopAsync();
            // Exclusive access proves both output pumps and their writer have released the log.
            using var log = File.Open(Path.Combine(logging, "GuardLifecycleTest.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.IsTrue(log.Length > 0);
        }
        finally { File.Delete(target); }
    }

    [TestMethod]
    public async Task NaturalExit_PreservesFinalLinesFromBothPipesAsync()
    {
        var bin = Path.Combine(Netch.Global.NetchDir, "bin");
        var logging = Path.Combine(Netch.Global.NetchDir, "logging");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(logging);
        var executable = "guard-test-" + Guid.NewGuid().ToString("N") + ".exe";
        var target = Path.Combine(bin, executable);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), target);
        try
        {
            var guard = new OutputGuard(executable);
            await guard.StartAsync();
            await guard.Instance.WaitForExitAsync();
            await guard.StopAsync();
            var text = File.ReadAllText(Path.Combine(logging, "GuardLifecycleTest.log"));
            StringAssert.Contains(text, "stdout-4000");
            StringAssert.Contains(text, "stderr-4000");
        }
        finally { File.Delete(target); }
    }
}
