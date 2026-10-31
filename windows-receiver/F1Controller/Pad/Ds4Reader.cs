using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HidSharp;

namespace F1Controller.Pad;

/// <summary>
/// Finds a DualShock 4 (USB or Bluetooth), keeps it in full-report mode and raises every parsed report.
/// Reconnects every second while the pad is absent.
/// </summary>
public sealed class Ds4Reader
{
    private const int SonyVid = 0x054C;
    private static readonly int[] Ds4Pids = { 0x05C4, 0x09CC, 0x0BA0 };

    public event Action<Ds4Report>? OnReport;

    public bool IsConnected { get; private set; }
    public bool IsBluetooth { get; private set; }

    /// <summary>Stopwatch.GetTimestamp() of the last parsed report.</summary>
    public long LastReportTicks { get; private set; }

    /// <summary>Battery % from the latest report.</summary>
    public byte Battery { get; private set; }

    /// <summary>Why the last connection ended, for the dashboard.</summary>
    public string? LastError { get; private set; }

    // HID reads block, so the loop gets its own thread instead of the caller's
    public Task RunAsync(CancellationToken ct) => Task.Run(() => RunLoopAsync(ct), CancellationToken.None);

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var device = DeviceList.Local.GetHidDevices(SonyVid)
                .FirstOrDefault(d => Ds4Pids.Contains(d.ProductID));

            if (device != null && device.TryOpen(out HidStream stream))
            {
                using (stream)
                {
                    try { ReadLoop(device, stream, ct); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Pad turned off or out of range: fall through and reconnect
                        LastError = ex.Message;
                    }
                }
            }

            IsConnected = false;
            try { await Task.Delay(1000, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void ReadLoop(HidDevice device, HidStream stream, CancellationToken ct)
    {
        IsBluetooth = device.GetMaxInputReportLength() > 64;
        stream.ReadTimeout = 100;
        var buffer = new byte[device.GetMaxInputReportLength()];
        if (IsBluetooth) RequestFullReports(device, stream);
        IsConnected = true;

        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = stream.Read(buffer, 0, buffer.Length); }
            catch (TimeoutException) { continue; }   // no report yet; the failsafe watches LastReportTicks
            if (n <= 0) continue;

            // Over Bluetooth the pad starts in reduced mode (0x01, no motion data) until asked for 0x11
            if (IsBluetooth && buffer[0] == 0x01)
            {
                RequestFullReports(device, stream);
                continue;
            }

            if (Ds4Report.TryParse(buffer.AsSpan(0, n), out var report))
            {
                LastReportTicks = Stopwatch.GetTimestamp();
                Battery = report.Battery;
                OnReport?.Invoke(report);
            }
        }
    }

    // Reading feature report 0x05 switches a Bluetooth DS4 to full 0x11 input reports
    private static void RequestFullReports(HidDevice device, HidStream stream)
    {
        var feature = new byte[Math.Max(device.GetMaxFeatureReportLength(), 64)];
        feature[0] = 0x05;
        try { stream.GetFeature(feature); }
        catch (Exception) { /* some stacks reject the read but still switch modes */ }
    }
}
