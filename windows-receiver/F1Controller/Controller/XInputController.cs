using System;
using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace F1Controller.Controller;

/// <summary>
/// Creates a virtual Xbox 360 controller via ViGEmBus and submits XInput reports
/// from incoming phone controller packets.
/// </summary>
public sealed class XInputController : IDisposable
{
    private readonly ViGEmClient _vigemClient;
    private readonly IXbox360Controller _controller;
    private readonly object _submitLock = new();

    /// <summary>Last measured time from packet-receive to XInput report submission, in microseconds.</summary>
    public double SubmitLatencyUs { get; private set; }

    /// <summary>Total reports submitted since startup.</summary>
    public long ReportsSubmitted { get; private set; }

    public bool IsConnected { get; private set; }

    public XInputController()
    {
        _vigemClient = new ViGEmClient();
        _controller = _vigemClient.CreateXbox360Controller();
        // Default is to submit a report on every Set* call (~20 IRPs per packet).
        // Batch them and submit once.
        _controller.AutoSubmitReport = false;
        _controller.FeedbackReceived += (_, e) => OnRumble?.Invoke(e.LargeMotor, e.SmallMotor);
    }

    /// <summary>The game set the rumble motors: (large/heavy, small/light), 0..255. Raised on a ViGEm thread.</summary>
    public event Action<byte, byte>? OnRumble;

    /// <summary>Plug in the virtual controller. Must be called before Submit.</summary>
    public void Connect()
    {
        _controller.Connect();
        IsConnected = true;
        Console.WriteLine("[ViGEm] Virtual Xbox 360 controller connected.");
    }

    /// <summary>
    /// Submit an XInput report built from the phone's control packet.
    /// Called on every packet arrival (~250 Hz).
    /// </summary>
    /// <param name="packet">Parsed control packet from the phone.</param>
    /// <param name="receiveTicks">Stopwatch.GetTimestamp() of when the UDP packet arrived.</param>
    public void Submit(Transport.ControlPacket packet, long receiveTicks)
    {
        if (!IsConnected) return;

        // Called from the receive loop and the failsafe timer
        lock (_submitLock)
        {
            // Keep Y axes centered so games don't see phantom input
            SubmitLocked(packet, 0, 0, 0, receiveTicks);
        }
    }

    /// <summary>
    /// Submit a report from the DS4 pad mode: steering on the left X axis, the pad's
    /// other stick axes passed through.
    /// </summary>
    public void SubmitPad(short steer, short leftY, short rightX, short rightY, byte brake, byte throttle, ushort buttons)
    {
        if (!IsConnected) return;
        var packet = new Transport.ControlPacket(0, 0, steer, throttle, brake, buttons);
        lock (_submitLock)
        {
            SubmitLocked(packet, leftY, rightX, rightY, Stopwatch.GetTimestamp());
        }
    }

    /// <summary>Centre the stick, release triggers and buttons.</summary>
    public void SubmitNeutral() => Submit(default, Stopwatch.GetTimestamp());

    private void SubmitLocked(Transport.ControlPacket packet, short leftY, short rightX, short rightY, long receiveTicks)
    {
        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, packet.Steer);
        _controller.SetAxisValue(Xbox360Axis.LeftThumbY, leftY);
        _controller.SetAxisValue(Xbox360Axis.RightThumbX, rightX);
        _controller.SetAxisValue(Xbox360Axis.RightThumbY, rightY);

        _controller.SetSliderValue(Xbox360Slider.RightTrigger, packet.Throttle);
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, packet.Brake);

        // Map button bitfield
        _controller.SetButtonState(Xbox360Button.A, packet.ButtonA);
        _controller.SetButtonState(Xbox360Button.B, packet.ButtonB);
        _controller.SetButtonState(Xbox360Button.X, packet.ButtonX);
        _controller.SetButtonState(Xbox360Button.Y, packet.ButtonY);
        _controller.SetButtonState(Xbox360Button.LeftShoulder, packet.ButtonLB);
        _controller.SetButtonState(Xbox360Button.RightShoulder, packet.ButtonRB);
        _controller.SetButtonState(Xbox360Button.Start, packet.ButtonStart);
        _controller.SetButtonState(Xbox360Button.Back, packet.ButtonBack);
        _controller.SetButtonState(Xbox360Button.LeftThumb, packet.ButtonLT);
        _controller.SetButtonState(Xbox360Button.RightThumb, packet.ButtonRT);
        _controller.SetButtonState(Xbox360Button.Up, packet.DPadUp);
        _controller.SetButtonState(Xbox360Button.Down, packet.DPadDown);
        _controller.SetButtonState(Xbox360Button.Left, packet.DPadLeft);
        _controller.SetButtonState(Xbox360Button.Right, packet.DPadRight);

        _controller.SubmitReport();

        // Measure submission latency
        // Both timestamps from Stopwatch.GetTimestamp(); previously each class had
        // its own Stopwatch, which made this difference meaningless.
        long nowTicks = Stopwatch.GetTimestamp();
        SubmitLatencyUs = (nowTicks - receiveTicks) * 1_000_000.0 / Stopwatch.Frequency;
        ReportsSubmitted++;
    }

    public void Disconnect()
    {
        if (IsConnected)
        {
            _controller.Disconnect();
            IsConnected = false;
            Console.WriteLine("[ViGEm] Virtual controller disconnected.");
        }
    }

    public void Dispose()
    {
        Disconnect();
        _vigemClient.Dispose();
    }
}
