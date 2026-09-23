using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio.Threading;
using Netch.Enums;
using Netch.Models;
using Netch.Utils;

namespace Netch.Controllers;

public abstract class Guard
{
    private FileStream? _logFileStream;
    private StreamWriter? _logStreamWriter;
    private bool _processStarted;
    private bool _stopped;
    private readonly SemaphoreSlim _logLock = new(1, 1);
    private Task _outputTask = Task.CompletedTask;
    private Task _errorTask = Task.CompletedTask;

    /// <param name="mainFile">application path relative of Netch\bin</param>
    /// <param name="redirectOutput"></param>
    /// <param name="encoding">application output encode</param>
    protected Guard(string mainFile, bool redirectOutput = true, Encoding? encoding = null)
    {
        RedirectOutput = redirectOutput;

        var fileName = Path.GetFullPath(Path.Combine(Global.NetchDir, "bin", mainFile));

        if (!File.Exists(fileName))
            throw new MessageException(i18N.Translate($"bin\\{mainFile} file not found!"));

        Instance = new Process
        {
            StartInfo =
            {
                FileName = fileName,
                WorkingDirectory = $"{Global.NetchDir}\\bin",
                CreateNoWindow = true,
                UseShellExecute = !RedirectOutput,
                RedirectStandardOutput = RedirectOutput,
                StandardOutputEncoding = RedirectOutput ? encoding : null,
                RedirectStandardError = RedirectOutput,
                StandardErrorEncoding = RedirectOutput ? encoding : null,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
    }

    protected string LogPath => Path.Combine(Global.NetchDir, $"logging\\{Name}.log");

    protected virtual IEnumerable<string> StartedKeywords { get; } = new List<string>();

    protected virtual IEnumerable<string> FailedKeywords { get; } = new List<string>();

    public abstract string Name { get; }

    private State State { get; set; } = State.Waiting;

    private bool RedirectOutput { get; }

    public Process Instance { get; }

    protected async Task StartGuardAsync(string argument, ProcessPriorityClass priority = ProcessPriorityClass.Normal)
    {
        State = State.Starting;

        _logFileStream = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, true);
        _logStreamWriter = new StreamWriter(_logFileStream) { AutoFlush = true };

        Instance.StartInfo.Arguments = argument;
        Instance.Start();
        _processStarted = true;
        Global.Job.AddProcess(Instance);

        if (priority != ProcessPriorityClass.Normal)
            Instance.PriorityClass = priority;

        if (RedirectOutput)
        {
            _outputTask = ReadOutputAsync(Instance.StandardOutput);
            _errorTask = ReadOutputAsync(Instance.StandardError);

            if (!StartedKeywords.Any())
            {
                // Skip, No started keyword
                State = State.Started;
                return;
            }

            // wait ReadOutput change State
            for (var i = 0; i < 1000; i++)
            {
                await Task.Delay(50);
                if (Instance.HasExited) State = State.Stopped;
                switch (State)
                {
                    case State.Started:
                        OnStarted();
                        return;
                    case State.Stopped:
                        await StopGuardAsync();
                        OnStartFailed();
                        throw new MessageException($"{Name}: {i18N.Translate("Core startup failed. See the core log in logging.")}");
                }
            }

            await StopGuardAsync();
            throw new MessageException($"{Name}: {i18N.Translate("Core startup timed out.")}");
        }
    }

    private async Task ReadOutputAsync(TextReader reader)
    {
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            await _logLock.WaitAsync().ConfigureAwait(false);
            try { await _logStreamWriter!.WriteLineAsync(line).ConfigureAwait(false); }
            finally { _logLock.Release(); }
            OnReadNewLine(line);

            if (State == State.Starting)
            {
                if (StartedKeywords.Any(s => line.Contains(s)))
                    State = State.Started;
                else if (FailedKeywords.Any(s => line.Contains(s)))
                {
                    State = State.Stopped;
                }
            }
        }
    }

    public virtual Task StopAsync()
    {
        return StopGuardAsync();
    }

    protected async Task StopGuardAsync()
    {
        if (_stopped) return;
        _stopped = true;
        try
        {
            if (_processStarted && !Instance.HasExited)
            {
                Instance.Kill();
                await Instance.WaitForExitAsync();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Stop {Name} failed", Name);
        }
        finally
        {
            // Drain both pipes before closing their shared writer.
            try
            {
#pragma warning disable VSTHRD003
                await Task.WhenAll(_outputTask, _errorTask);
#pragma warning restore VSTHRD003
            }
            catch (Exception exception) { Log.Warning(exception, "Could not drain {Name} output", Name); }
            if (_logStreamWriter != null)
                await _logStreamWriter.DisposeAsync();

            if (_logFileStream != null)
                await _logFileStream.DisposeAsync();

            Instance.Dispose();

            State = State.Stopped;
        }
    }

    protected virtual void OnStarted()
    {
    }

    protected virtual void OnReadNewLine(string line)
    {
    }

    protected virtual void OnStartFailed()
    {
        Utils.Utils.Open(LogPath);
    }
}
