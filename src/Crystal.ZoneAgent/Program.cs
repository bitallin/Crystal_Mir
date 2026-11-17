using System.Runtime.InteropServices;

namespace Crystal.ZoneAgent;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        AgentOptions options;
        try
        {
            options = AgentOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            AgentOptions.PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            AgentOptions.PrintUsage();
            return 0;
        }

        using var shutdown = new CancellationTokenSource();
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            shutdown.Cancel();
        });
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            shutdown.Cancel();
        });

        Console.WriteLine(
            $"{DateTimeOffset.UtcNow:O} [AGENT] Starting zone {options.ZoneId} " +
            $"(agent {options.AgentId}, control {options.ControlApiUrl}).");
        using var runner = new AgentRunner(options);
        try
        {
            await runner.RunAsync(shutdown.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} [AGENT] FATAL {exception}");
            return 1;
        }
    }
}
