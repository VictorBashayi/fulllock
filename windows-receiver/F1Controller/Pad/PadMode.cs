using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using F1Controller.Controller;

namespace F1Controller.Pad;

/// <summary>
/// <c>--ds4</c>: drive the virtual Xbox pad from a DualShock 4 held flat and banked to steer.
/// No phone, telemetry or engineer in this mode.
/// </summary>
public static class PadMode
{
    private const double BiasSeconds = 1.0;
    private const double ReportTimeoutMs = 100;

    /// <summary>DS4 stick byte (0..255, centre 128) → XInput axis. DS4 Y is 0 at the top, XInput Y is positive up.</summary>
    public static short StickAxis(byte raw, bool invert)
    {
        int v = (raw - 128) * 258;
        if (invert) v = -v;
        return (short)Math.Clamp(v, short.MinValue, short.MaxValue);
    }

    public static async Task<int> RunAsync(bool noVigem, bool interactive)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        Console.WriteLine("F1 Controller — DS4 pad mode");
        Console.WriteLine("============================");
        foreach (var name in new[] { "DS4Windows", "JoyShockMapper" })
            if (Process.GetProcessesByName(name).Length > 0)
                Console.WriteLine($"[Warning] {name} is running — close it, it will fight over the pad.");

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
                xinput = null;
            }
        }

        var settings = PadSettings.Load(PadSettings.DefaultPath);
        var steering = new PadSteering(settings);
        var reader = new Ds4Reader();

        // Reader thread only: bias window on the first connection, then steer every report
        long biasUntil = 0;
        bool calibrated = false;
        short steer = 0;
        reader.OnReport += r =>
        {
            long now = Stopwatch.GetTimestamp();
            if (!calibrated)
            {
                if (biasUntil == 0) biasUntil = now + (long)(BiasSeconds * Stopwatch.Frequency);
                if (now < biasUntil) { steering.AddBiasSample(r); steer = 0; }
                else { steering.FinishBias(); calibrated = true; }
            }
            if (calibrated) steer = steering.Process(r);

            xinput?.SubmitPad(steer,
                StickAxis(r.LY, invert: true), StickAxis(r.RX, invert: false), StickAxis(r.RY, invert: true),
                r.L2, r.R2, r.Buttons);
        };

        // Failsafe: no report for 100 ms (pad off, out of range) → release everything once
        bool released = true;
        using var failsafe = new Timer(_ =>
        {
            double ageMs = (Stopwatch.GetTimestamp() - reader.LastReportTicks) * 1000.0 / Stopwatch.Frequency;
            bool stale = reader.LastReportTicks == 0 || ageMs > ReportTimeoutMs;
            if (stale && !released) { xinput?.SubmitNeutral(); steer = 0; released = true; }
            else if (!stale) released = false;
        }, null, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20));

        var dashboard = new PadDashboard(reader, steering, settings, xinput);
        var readerTask = reader.RunAsync(cts.Token);

        if (interactive) Console.Clear();
        while (!cts.Token.IsCancellationRequested)
        {
            if (!Console.IsInputRedirected)
                while (Console.KeyAvailable)
                    if (HandleKey(Console.ReadKey(intercept: true).KeyChar, settings))
                        settings.Save(PadSettings.DefaultPath);

            string status = reader.IsConnected && !released
                ? (calibrated ? "Driving" : "Keep the pad still — calibrating…")
                : reader.LastReportTicks != 0 ? "Pad lost — reconnecting" : "Waiting for DS4…";
            try
            {
                if (interactive) dashboard.Render(status, steer);
                else dashboard.WriteStatusLine(status, steer);
                await Task.Delay(interactive ? 67 : 1000, cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch { /* console resize etc. */ }
        }

        await readerTask;
        xinput?.Dispose();
        if (interactive) Console.CursorVisible = true;
        Console.WriteLine();
        Console.WriteLine("Done.");
        return 0;
    }

    /// <returns>true when a setting changed.</returns>
    private static bool HandleKey(char key, PadSettings s)
    {
        switch (key)
        {
            case '[': s.NudgeLock(-1); return true;
            case ']': s.NudgeLock(1); return true;
            case '-': s.NudgeDeadzone(-1); return true;
            case '=': s.NudgeDeadzone(1); return true;
            case ',': s.NudgeCurve(-1); return true;
            case '.': s.NudgeCurve(1); return true;
            default: return false;
        }
    }
}
