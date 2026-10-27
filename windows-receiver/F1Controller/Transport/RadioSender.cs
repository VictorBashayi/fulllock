using System;
using System.Threading;
using System.Threading.Tasks;
using F1Controller.Engineer;

namespace F1Controller.Transport;

/// <summary>
/// Sends engineer calls to the phone. UDP can drop a packet, so each call goes out
/// <see cref="Repeats"/> times ~30 ms apart with the same call ID; the phone ignores repeats.
/// </summary>
public sealed class RadioSender
{
    private const int Repeats = 3;
    private static readonly TimeSpan RepeatGap = TimeSpan.FromMilliseconds(30);

    private readonly UdpReceiver _receiver;
    private readonly Action<string>? _log;
    private int _callId;

    /// <param name="log">Called with each call's text; null when the console dashboard shows it instead.</param>
    public RadioSender(UdpReceiver receiver, Action<string>? log = null)
    {
        _receiver = receiver;
        _log = log;
    }

    /// <summary>Text of the most recent call, for the console dashboard.</summary>
    public string? LastText { get; private set; }

    public void Send(RadioCall call)
    {
        var buf = new byte[ControlPacket.RadioHeaderSize + ControlPacket.RadioMaxText];
        ushort id = unchecked((ushort)Interlocked.Increment(ref _callId));
        int n = ControlPacket.WriteRadioCall(buf, id, call);
        var packet = buf[..n];  // SendToPhone sends the whole array

        LastText = call.Text;
        _log?.Invoke(call.Text);

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < Repeats; i++)
            {
                if (i > 0) await Task.Delay(RepeatGap).ConfigureAwait(false);
                _receiver.SendToPhone(packet);
            }
        });
    }
}
