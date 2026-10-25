using System;
using System.Threading;
using System.Threading.Tasks;
using F1Controller.Controller;
using F1Controller.Transport;
using F1Controller.UI;
using F1Controller.Validation;

namespace F1Controller;

class Program
{
    static async Task<int> Main(string[] args)
    {
        int port = 5555;
        bool validate = false;
        bool logTransport = false;
        bool noVigem = false;

        // Simple argument parsing
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out port) || port < 1 || port > 65535)
                    {
                        Console.Error.WriteLine("Invalid port number.");
                        return 1;
                    }
                    break;
                case "--validate":
                    validate = true;
                    break;
                case "--log-transport":
                    logTransport = true;
                    break;
                case "--no-vigem":
                    noVigem = true;
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    PrintUsage();
                    return 1;
            }
        }

        bool interactive = !Console.IsOutputRedirected;
        if (interactive)
        {
            Console.Clear();
            Console.CursorVisible = false;
            Console.Title = "F1 Phone Controller — Windows Receiver";
        }

        Console.WriteLine("F1 Phone Controller — Windows Receiver");
        Console.WriteLine("======================================");
        Console.WriteLine($"  Port:       {port}");
        Console.WriteLine($"  ViGEm:      {(noVigem ? "disabled" : "enabled")}");
        Console.WriteLine($"  Validate:   {(validate ? "yes (F1 telemetry on 20777)" : "no")}");
        Console.WriteLine($"  Log CSV:    {(logTransport ? "yes" : "no")}");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        // --- Initialize components ---

        var receiver = new UdpReceiver(port, logTransport);

        XInputController? xinput = null;
        if (!noVigem)
        {
            try
            {
                xinput = new XInputController();
                xinput.Connect();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ViGEm] Failed to initialize: {ex.Message}");
                Console.WriteLine("[ViGEm] Is ViGEmBus driver installed? https://github.com/nefarius/ViGEmBus/releases");
                Console.WriteLine("[ViGEm] Continuing without virtual controller...");
                xinput = null;
            }
        }

        CorrelationLogger? correlation = null;
        TelemetryReceiver? telemetry = null;

        if (validate)
        {
            correlation = new CorrelationLogger();
            telemetry = new TelemetryReceiver(20777);
        }

        // Wire packet reception to ViGEm submission
        receiver.OnPacketReceived += (packet, receiveTicks) =>
        {
            xinput?.Submit(packet, receiveTicks);

            // If validating, match sent values with latest game telemetry
            if (correlation != null && telemetry?.HasData == true)
            {
                correlation.Record(
                    packet.Steer, telemetry.GameSteer,
                    packet.Throttle, telemetry.GameThrottle,
                    packet.Brake, telemetry.GameBrake);
            }
        };

        // Failsafe: if the phone stops sending (Wi-Fi drop, app backgrounded),
        // release all inputs instead of holding the last throttle/steer forever.
        bool wasReceiving = false;
        using var failsafe = new Timer(_ =>
        {
            bool receiving = receiver.IsReceiving;
            if (wasReceiving && !receiving)
                xinput?.SubmitNeutral();
            wasReceiving = receiving;
        }, null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));

        var dashboard = new ConsoleDashboard(receiver, xinput, correlation);

        // --- Start tasks ---

        var tasks = new List<Task>();
        tasks.Add(receiver.RunAsync(cts.Token));

        if (telemetry != null)
            tasks.Add(telemetry.RunAsync(cts.Token));

        // Dashboard refresh loop (~15 Hz)
        tasks.Add(Task.Run(async () =>
        {
            // Wait a moment for initial output to clear
            await Task.Delay(1000, cts.Token).ConfigureAwait(false);
            if (interactive) Console.Clear();

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (interactive)
                    {
                        dashboard.Render();
                        await Task.Delay(67, cts.Token); // ~15 fps
                    }
                    else
                    {
                        // Redirected output (e.g. VS Code debug console): one line per second
                        dashboard.WriteStatusLine();
                        await Task.Delay(1000, cts.Token);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    // Swallow rendering errors (console resize, etc.)
                }
            }
        }, cts.Token));

        Console.WriteLine("Waiting for phone connection... (Ctrl+C to exit)");

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        // Cleanup
        if (interactive) Console.CursorVisible = true;
        Console.WriteLine();
        Console.WriteLine("Shutting down...");

        xinput?.Dispose();
        correlation?.Dispose();
        telemetry?.Dispose();
        receiver.Dispose();

        // Print final stats if validation was running
        if (correlation != null && correlation.SampleCount > 0)
        {
            Console.WriteLine();
            Console.WriteLine("=== Final Validation Results ===");
            Console.WriteLine($"  Samples:      {correlation.SampleCount}");
            Console.WriteLine($"  Steer R²:     {correlation.SteerR2:F6}");
            Console.WriteLine($"  Steer MAE:    {correlation.SteerMAE:F6}");
            Console.WriteLine($"  Throttle R²:  {correlation.ThrottleR2:F6}");
            Console.WriteLine($"  Brake R²:     {correlation.BrakeR2:F6}");
            Console.WriteLine();

            bool pass = correlation.SteerR2 > 0.99 && correlation.SteerMAE < 0.05;
            Console.WriteLine(pass
                ? "✓ PASS: Steering correlation meets targets (R² > 0.99, MAE < 0.05)"
                : "✗ FAIL: Steering correlation below targets");
        }

        Console.WriteLine("Done.");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            F1 Phone Controller — Windows Receiver

            Usage: F1Controller.exe [options]

            Options:
              --port <N>        UDP listen port (default: 5555)
              --validate        Enable F1 telemetry validation (port 20777)
              --log-transport   Log transport metrics to CSV
              --no-vigem        Disable virtual controller (transport only)
              -h, --help        Show this help

            Examples:
              F1Controller.exe                         Normal mode
              F1Controller.exe --port 6000             Custom port
              F1Controller.exe --validate              With F1 telemetry correlation
              F1Controller.exe --no-vigem --log-transport   Transport diagnostics only
            """);
    }
}
