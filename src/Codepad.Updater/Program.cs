using System.Diagnostics;
using System.Text.Json;

namespace Codepad.Updater;

/// <summary>The real machine: processes, the clock and delays.</summary>
internal sealed class SystemHost : IUpdateHost
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    public bool IsRunning(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public IStartedProcess Start(string path, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        ProcessStartInfo info = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach ((string name, string value) in environment)
        {
            info.Environment[name] = value;
        }

        return new SystemProcess(Process.Start(info) ?? throw new InvalidOperationException("The process did not start."));
    }

    public Task DelayAsync(TimeSpan time, CancellationToken cancellationToken) => Task.Delay(time, cancellationToken);

    private sealed class SystemProcess(Process process) : IStartedProcess
    {
        public bool HasExited => process.HasExited;

        public void KillTree()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process ended on its own.
            }
        }
    }
}

/// <summary>The entry point of the helper: <c>Codepad.Updater plan.json</c>.</summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            await Console.Error.WriteLineAsync("Usage: Codepad.Updater <plan.json>");
            return UpdateSwap.INVALID_PLAN;
        }

        UpdatePlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<UpdatePlan>(await File.ReadAllTextAsync(args[0]), JsonSerializerOptions.Web);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            await Console.Error.WriteLineAsync("The plan cannot be read.");
            return UpdateSwap.INVALID_PLAN;
        }

        if (plan is null)
        {
            return UpdateSwap.INVALID_PLAN;
        }

        UpdateSwap swap = new UpdateSwap(new SystemHost(), message => Console.Error.WriteLine("codepad-updater: " + message));
        return await swap.RunAsync(plan, CancellationToken.None);
    }
}
