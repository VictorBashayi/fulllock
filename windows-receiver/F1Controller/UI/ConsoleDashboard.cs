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
    private readonly RadioSender? _radio;
    private readonly StringBuilder _sb = new(1024);
    private int _lastHeight;

    public ConsoleDashboard(UdpReceiver receiver,
                            Controller.XInputController? xinput,
                            Validation.CorrelationLogger? correlation,
                            RadioSender? radio = null)
    {
        _receiver = receiver;
        _xinput = xinput;
        _correlation = correlation;
        _radio = radio;
    }

    public void Render()
    {
        var pkt = _receiver.LastPacket;
        bool connected = _receiver.IsReceiving;
        string status = connected ? "Connected"
            : _receiver.RemoteAddress != null ? "Signal lost — inputs released"
            : "Waiting for phone...";
        string phoneIp = _receiver.RemoteAddress ?? "—";
        if (!connected) pkt = default;

        // Steering as % of full lock (lock angle is configured on the phone)
        double steerPct = pkt.Steer * 100.0 / 32767.0;
        double throttlePct = pkt.Throttle * 100.0 / 255.0;
        double brakePct = pkt.Brake * 100.0 / 255.0;

        string steerBar = BuildBar(Math.Abs(steerPct) / 100.0, 8, steerPct < 0);
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
        _sb.AppendLine($"║  Packets/sec:  {Pad((connected ? _receiver.PacketsPerSecond : 0).ToString(), 30)} ║");
        _sb.AppendLine($"║  Packet loss:  {Pad($"{_receiver.PacketLossPercent:F2}%", 30)} ║");
        _sb.AppendLine($"║  Jitter:       {Pad($"{_receiver.JitterUs:F1} µs", 30)} ║");
        _sb.AppendLine("╠═══════════════════════════════════════════════╣");
        _sb.AppendLine($"║  Steer:     {Pad($"{steerBar}  {steerPct,6:+0.0;-0.0;0.0}%", 34)}║");
        _sb.AppendLine($"║  Throttle:  {Pad($"{throttleBar}  {throttlePct,3:F0}%", 34)}║");
        _sb.AppendLine($"║  Brake:     {Pad($"{brakeBar}  {brakePct,3:F0}%", 34)}║");
        _sb.AppendLine("╠═══════════════════════════════════════════════╣");
        _sb.AppendLine($"║  ViGEm:        {Pad(vigemStatus, 30)} ║");
        _sb.AppendLine($"║  Submit lat:   {Pad($"{submitLatMs:F3} ms", 30)} ║");
        if (_radio != null)
            _sb.AppendLine($"║  Engineer:     {Pad(_radio.LastText ?? "—", 30)} ║");

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

    /// <summary>Compact single-line status for non-interactive output.</summary>
    public void WriteStatusLine()
    {
        bool connected = _receiver.IsReceiving;
        var pkt = connected ? _receiver.LastPacket : default;
        Console.WriteLine(
            $"[{DateTime.Now:HH:mm:ss}] {(connected ? "connected" : "waiting")} " +
            $"pps={(connected ? _receiver.PacketsPerSecond : 0)} loss={_receiver.PacketLossPercent:F2}% " +
            $"jitter={_receiver.JitterUs:F0}us steer={pkt.Steer} thr={pkt.Throttle} brk={pkt.Brake} " +
            $"btn=0x{pkt.Buttons:X4} submit={(_xinput?.SubmitLatencyUs ?? 0):F0}us");
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
