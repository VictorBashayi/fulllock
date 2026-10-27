using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace F1Controller.Transport;

/// <summary>
/// Broadcasts a discovery beacon (see <see cref="ControlPacket.WriteDiscovery"/>) to
/// every IPv4 network the PC is on, once a second. The phone reads the PC's address
/// from where the beacon came from, so a hotspot handing out a new IP doesn't break
/// the connection.
/// </summary>
public sealed class DiscoveryBeacon
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly int _controlPort;

    public DiscoveryBeacon(int controlPort)
    {
        _controlPort = controlPort;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        var beacon = new byte[ControlPacket.DiscoverySize];
        ControlPacket.WriteDiscovery(beacon, _controlPort);

        while (!ct.IsCancellationRequested)
        {
            // Re-read every time: Wi-Fi reconnects and hotspots change the networks
            foreach (var address in BroadcastAddresses())
            {
                try
                {
                    await client.SendAsync(beacon, new IPEndPoint(address, ControlPacket.DiscoveryPort), ct);
                }
                catch (OperationCanceledException) { return; }
                catch (SocketException) { } // network went away mid-loop
            }

            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>The subnet broadcast address of each IPv4 network on an interface that's up.</summary>
    private static List<IPAddress> BroadcastAddresses()
    {
        var result = new List<IPAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork && addr.IPv4Mask != null)
                    result.Add(BroadcastAddress(addr.Address, addr.IPv4Mask));
            }
        }
        return result;
    }

    /// <summary>The address with all host bits set, e.g. 192.168.1.20/24 → 192.168.1.255.</summary>
    public static IPAddress BroadcastAddress(IPAddress ip, IPAddress mask)
    {
        var ipBytes = ip.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        var result = new byte[4];
        for (int i = 0; i < 4; i++)
            result[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
        return new IPAddress(result);
    }
}
