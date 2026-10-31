using System;
using System.Text;
using F1Controller.Controller;

namespace F1Controller.Pad;

/// <summary>Console screen for DS4 pad mode, same box style as the phone dashboard.</summary>
public sealed class PadDashboard
{
    private readonly Ds4Reader _reader;
    private readonly PadSteering _steering;
    private readonly PadSettings _settings;
    private readonly XInputController? _xinput;
    private readonly StringBuilder _sb = new();

    public PadDashboard(Ds4Reader reader, PadSteering steering, PadSettings settings, XInputController? xinput)
    {
        _reader = reader;
        _steering = steering;
        _settings = settings;
        _xinput = xinput;
    }

    public void Render(string status, short steer)
    {
        double steerPct = steer * 100.0 / short.MaxValue;
        string link = _reader.IsConnected
            ? $"{(_reader.IsBluetooth ? "Bluetooth" : "USB")}, battery {_reader.Battery}%"
            : "—";

        _sb.Clear();
        _sb.AppendLine("╔═══════════════════════════════════════════════╗");
        _sb.AppendLine("║   F1 Controller — DS4 pad mode                ║");
        _sb.AppendLine("╠═══════════════════════════════════════════════╣");
        _sb.AppendLine($"║  Status:       {Pad(status, 30)} ║");
        _sb.AppendLine($"║  Link:         {Pad(link, 30)} ║");
        _sb.AppendLine($"║  Last error:   {Pad(_reader.LastError ?? "—", 30)} ║");
        _sb.AppendLine("╠═══════════════════════════════════════════════╣");
        _sb.AppendLine($"║  Steer:        {Pad($"{steerPct,6:+0.0;-0.0;0.0}%", 30)} ║");
        _sb.AppendLine($"║  Bank:         {Pad($"{_steering.LastBankDeg,6:+0.0;-0.0;0.0}°  (accel only {_steering.LastAccelBankDeg:+0.0;-0.0;0.0}°)", 30)} ║");
        _sb.AppendLine("╠═══════════════════════════════════════════════╣");
        _sb.AppendLine($"║  [ ] Lock:     {Pad($"{_settings.LockDeg:F0}° lean = full lock", 30)} ║");
        _sb.AppendLine($"║  - = Deadzone: {Pad($"{_settings.DeadzoneDeg:F1}°", 30)} ║");
        _sb.AppendLine($"║  , . Curve:    {Pad($"{_settings.Curve:F2}", 30)} ║");
        _sb.AppendLine("╠═══════════════════════════════════════════════╣");
        _sb.AppendLine($"║  ViGEm:        {Pad(_xinput?.IsConnected == true ? $"Active — {_xinput.ReportsSubmitted} reports" : "Disabled", 30)} ║");
        _sb.AppendLine("╚═══════════════════════════════════════════════╝");
        _sb.AppendLine("  Press Ctrl+C to exit");

        try { Console.SetCursorPosition(0, 0); }
        catch { Console.Clear(); }
        Console.Write(_sb.ToString());
    }

    /// <summary>Compact single-line status for non-interactive output.</summary>
    public void WriteStatusLine(string status, short steer) =>
        Console.WriteLine(
            $"[{DateTime.Now:HH:mm:ss}] {status} steer={steer} bank={_steering.LastBankDeg:F1} " +
            $"accelBank={_steering.LastAccelBankDeg:F1} lock={_settings.LockDeg} dz={_settings.DeadzoneDeg} curve={_settings.Curve}");

    private static string Pad(string s, int width) =>
        s.Length >= width ? s[..width] : s + new string(' ', width - s.Length);
}
