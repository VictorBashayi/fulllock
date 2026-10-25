using System;
using System.Text;
using F1Controller.Transport;

namespace F1Controller.UI;

/// <summary>
/// Renders a live console dashboard showing connection status,
/// transport metrics, controller state, and validation results.
/// </summary>
public sealed class ConsoleDashboard
{
    private readonly UdpReceiver _receiver;
    private readonly Controller.XInputController? _xinput;
    private readonly Validation.CorrelationLogger? _correlation;
    private readonly StringBuilder _sb = new(1024);
    private int _lastHeight;

    public ConsoleDashboard(UdpReceiver receiver,
                            Controller.XInputController? xinput,
                            Validation.CorrelationLogger? correlation)
    {
        _receiver = receiver;
        _xinput = xinput;
        _correlation = correlation;
    }

    public void Render()
    {
        var pkt = _receiver.LastPacket;
        bool connected = _receiver.IsReceiving;
        string status = connected ? "Connected" : "Waiting for phone...";
        string phoneIp = _receiver.RemoteAddress ?? "—";

        // Convert steer i16 to degrees (assuming ±90° lock = ±32768)
        double steerDeg = pkt.Steer * 90.0 / 32768.0;
        double throttlePct = pkt.Throttle * 100.0 / 255.0;
        double brakePct = pkt.Brake * 100.0 / 255.0;

        string steerBar = BuildBar(Math.Abs(steerDeg) / 90.0, 16, steerDeg < 0);
        string throttleBar = BuildFillBar(throttlePct / 100.0, 14);
        string brakeBar = BuildFillBar(brakePct / 100.0, 14);

        string vigemStatus = _xinput?.IsConnected == true
            ? $"Active (Xbox 360) — {_xinput.ReportsSubmitted} reports"
            : "Disabled";

        double submitLatMs = (_xinput?.SubmitLatencyUs ?? 0) / 1000.0;

        _sb.Clear();
        _sb.AppendLine("╔═══════════════════════════════════════════════╗");
        _sb.AppendLine("║   F1 Phone Controller — Windows Receiver      ║");
        _sb.AppendLine("╠═══════════════════════════════════════════════╣");
        _sb.AppendLine($"║  Status:       {Pad(status, 30)} ║");
        _sb.AppendLine($"║  Phone IP:     {Pad(phoneIp, 30)} ║");
        _sb.AppendLine($"║  Packets/sec:  {Pad(_receiver.PacketsPerSecond.ToString(), 30)} ║");
        _sb.AppendLine($"║  Packet loss:  {Pad($"{_receiver.PacketLossPercent:F2}%", 30)} ║");
        _sb.AppendLine($"║  Jitter:       {Pad($"{_receiver.JitterUs:F1} µs", 30)} ║");
        _sb.AppendLine("╠═══════════════════════════════════════════════╣");
        _sb.AppendLine($"║  Steer:     {steerBar}  {(steerDeg >= 0 ? "+" : "")}{steerDeg:F1}°{Pad("", 30 - steerBar.Length - 7)}║");
        _sb.AppendLine($"║  Throttle:  {throttleBar}  {throttlePct:F0}%{Pad("", 30 - throttleBar.Length - 5)}║");
        _sb.AppendLine($"║  Brake:     {brakeBar}  {brakePct:F0}%{Pad("", 30 - brakeBar.Length - 5)}║");
        _sb.AppendLine("╠═══════════════════════════════════════════════╣");
        _sb.AppendLine($"║  ViGEm:        {Pad(vigemStatus, 30)} ║");
        _sb.AppendLine($"║  Submit lat:   {Pad($"{submitLatMs:F3} ms", 30)} ║");

        if (_correlation != null && _correlation.SampleCount > 0)
        {
            _sb.AppendLine("╠═══════════════════════════════════════════════╣");
            _sb.AppendLine($"║  Steer R²:     {Pad($"{_correlation.SteerR2:F4}", 30)} ║");
            _sb.AppendLine($"║  Steer MAE:    {Pad($"{_correlation.SteerMAE:F4}", 30)} ║");
            _sb.AppendLine($"║  Samples:      {Pad(_correlation.SampleCount.ToString(), 30)} ║");
        }

        _sb.AppendLine("╚═══════════════════════════════════════════════╝");
        _sb.AppendLine("  Press Ctrl+C to exit");

        // Move cursor to top and overwrite
        try
        {
            Console.SetCursorPosition(0, 0);
        }
        catch
        {
            Console.Clear();
        }
        Console.Write(_sb.ToString());

        // Clear any leftover lines from previous render
        int currentHeight = _sb.ToString().Split('\n').Length;
        if (currentHeight < _lastHeight)
        {
            for (int i = 0; i < _lastHeight - currentHeight; i++)
                Console.WriteLine(new string(' ', 50));
        }
        _lastHeight = currentHeight;
    }

    private static string Pad(string s, int width)
    {
        return s.Length >= width ? s[..width] : s + new string(' ', width - s.Length);
    }

    private static string BuildBar(double fraction, int width, bool leftSide)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        int filled = (int)(fraction * width);
        int empty = width - filled;

        if (leftSide)
            return new string('░', empty) + new string('█', filled) + "┃" + new string('░', width);
        else
            return new string('░', width) + "┃" + new string('█', filled) + new string('░', empty);
    }

    private static string BuildFillBar(double fraction, int width)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        int filled = (int)(fraction * width);
        int empty = width - filled;
        return new string('█', filled) + new string('░', empty);
    }
}
