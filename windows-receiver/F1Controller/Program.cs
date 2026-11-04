using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using F1Controller.Controller;
using F1Controller.Engineer;
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
        bool noTelemetry = false;
        bool radioDemo = false;
        bool ds4 = false;
        bool hidHideCleanup = false;

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
                case "--no-telemetry":
                    noTelemetry = true;
                    break;
                case "--radio-demo":
                    radioDemo = true;
                    break;
                case "--ds4":
                    ds4 = true;
                    break;
                case "--hidhide-cleanup":
                    hidHideCleanup = true;
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

        // Uninstaller: undo FullLock's HidHide changes and remove it from the whitelist
        if (hidHideCleanup)
        {
            F1Controller.Pad.PadMode.CreateHidHideGuard(AppPaths.Default, Console.WriteLine)?.Cleanup();
            return 0;
        }

        bool interactive = !Console.IsOutputRedirected;
        if (interactive)
        {
            Console.Clear();
            Console.CursorVisible = false;
            Console.Title = "FullLock — Windows Receiver";
        }

        // The game saturates CPU/GPU; at normal priority our receive loop waits
        // behind it, which adds to ping RTT and input latency.
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; }
        catch (Exception ex) { Console.WriteLine($"[Priority] Couldn't raise priority: {ex.Message}"); }

        // Pad mode replaces the phone entirely: no UDP, discovery, telemetry or engineer
        if (ds4)
            return await F1Controller.Pad.PadMode.RunAsync(noVigem, interactive);

        Console.WriteLine("FullLock — Windows Receiver");
        Console.WriteLine("===========================");
        Console.WriteLine($"  Port:       {port} (announced on UDP {ControlPacket.DiscoveryPort} so the phone finds this PC)");
        Console.WriteLine($"  ViGEm:      {(noVigem ? "disabled" : "enabled")}");
        Console.WriteLine($"  Telemetry:  {(noTelemetry && !validate ? "off" : "F1 game UDP on 20777 → phone dashboard")}");
        Console.WriteLine($"  Validate:   {(validate ? "yes" : "no")}");
        Console.WriteLine($"  Log CSV:    {(logTransport ? "yes" : "no")}");
        AppPaths.Default.EnsureCreated();
        Console.WriteLine($"  Logs:       {AppPaths.Default.LogsDir}");
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
            if (!Drivers.ViGEmBusInstalled)
                return Drivers.ReportViGEmMissing(interactive);
            try
            {
                xinput = new XInputController();
                xinput.Connect();
            }
            catch (Exception ex)
            {
                return Drivers.ReportViGEmMissing(interactive, $"Failed to initialize: {ex.Message}");
            }
        }

        CorrelationLogger? correlation = null;
        TelemetryReceiver? telemetry = null;

        if (validate)
            correlation = new CorrelationLogger();
        if (validate || !noTelemetry)
            telemetry = new TelemetryReceiver(20777);

        var disposables = new List<IDisposable>();

        // The race engineer's calls go to the phone (and on the dashboard / log)
        var radio =new RadioSender(receiver, interactive ? null : text => Console.WriteLine($"[Engineer] {text}"));

        // Forward gear/revs/DRS/ERS to the phone as the game reports them.
        // Called on the telemetry thread only, so one buffer is enough.
        if (telemetry != null && !noTelemetry)
        {
            // Every call (and any engineer error) also goes to a log file for reviewing the race
            var callLogWriter = new StreamWriter(AppPaths.Default.LogFile("engineer_log", "txt"), append: false);
            disposables.Add(callLogWriter);
            var callLog = new CallLog(callLogWriter);
            var engineer = new EngineerService(new EngineerThresholds(),
                call => { radio.Send(call); callLog.Write(call); },
                callLog.WriteError,
                level: () => receiver.EngineerLevel,
                log: callLog.WriteNote);
            telemetry.OnSnapshot += engineer.OnSnapshot;

            var stateBuf = new byte[ControlPacket.GameStateSize];
            telemetry.OnStateChanged += state =>
            {
                ControlPacket.WriteGameState(stateBuf, state);
                receiver.SendToPhone(stateBuf);
            };
            var raceBuf = new byte[ControlPacket.RaceStateSize];
            telemetry.OnRaceStateChanged += race =>
            {
                ControlPacket.WriteRaceState(raceBuf, race);
                receiver.SendToPhone(raceBuf);
            };
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

        var dashboard = new ConsoleDashboard(receiver, xinput, correlation, radio);

        // --- Start tasks ---

        var tasks = new List<Task>();
        tasks.Add(receiver.RunAsync(cts.Token));
        tasks.Add(new DiscoveryBeacon(port).RunAsync(cts.Token));

        if (telemetry != null)
            tasks.Add(telemetry.RunAsync(cts.Token));

        if (radioDemo)
            tasks.Add(RadioDemoAsync(receiver, radio, cts.Token));

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
        foreach (var d in disposables) d.Dispose();

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

    /// <summary>Once the phone connects, sends one sample call of each priority, 5 s apart.</summary>
    private static async Task RadioDemoAsync(UdpReceiver receiver, RadioSender radio, CancellationToken ct)
    {
        var calls = new[]
        {
            new RadioCall("demo-chatter", "Purple sector.", RadioPriority.Chatter, RadioCategory.Lap),
            new RadioCall("demo-info", "DRS enabled.", RadioPriority.Info, RadioCategory.Other),
            new RadioCall("demo-important", "Front left is overheating, manage it.", RadioPriority.Important, RadioCategory.Tyres),
            new RadioCall("demo-critical", "Safety car deployed.", RadioPriority.Critical, RadioCategory.Flags),
        };
        try
        {
            while (!receiver.IsReceiving)
                await Task.Delay(500, ct);
            foreach (var call in calls)
            {
                await Task.Delay(5000, ct);
                radio.Send(call);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            FullLock — Windows Receiver

            Usage: FullLock.exe [options]

            Options:
              --port <N>        UDP listen port (default: 5555)
              --validate        Enable F1 telemetry validation (port 20777)
              --log-transport   Log transport metrics to CSV
              --no-vigem        Disable virtual controller (transport only)
              --no-telemetry    Don't listen for game telemetry / send it to the phone
              --radio-demo      Send one sample engineer call of each priority once the phone connects
              --ds4             Drive with a DualShock 4 held flat (bank to steer) instead of the phone
              --hidhide-cleanup Undo FullLock's HidHide changes (used by the uninstaller)
              -h, --help        Show this help

            Examples:
              FullLock.exe                             Normal mode
              FullLock.exe --port 6000                 Custom port
              FullLock.exe --validate                  With F1 telemetry correlation
              FullLock.exe --no-vigem --log-transport   Transport diagnostics only
            """);
    }
}
