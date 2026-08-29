using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RemoteShutdown.Agent.Core.Network;

/// <summary>
/// Surfaces the MAC address and a broadcast-address hint returned to the phone during
/// pairing (docs/protocol.md §4) so it can send Wake-on-LAN magic packets later without
/// the agent's involvement (it's unreachable while the PC is off).
/// </summary>
public static class NetworkInfoService
{
    public static string? GetPrimaryMacAddress()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.GetPhysicalAddress().GetAddressBytes().Length == 6)
            .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            .FirstOrDefault();

        var bytes = nic?.GetPhysicalAddress().GetAddressBytes();
        return bytes is null ? null : string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    /// <summary>
    /// Лучший на данный момент способ определить IPv4-адрес ПК в локальной сети — нужен
    /// для QR-кода пейринга (docs/roadmap.md, "QR-код пейринг"): телефон сканирует
    /// host+port вместо ручного ввода IP.
    /// </summary>
    public static string? GetPrimaryIPv4Address()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    return addr.Address.ToString();
            }
        }
        return null;
    }

    /// <summary>Best-effort broadcast address for the primary IPv4 interface (e.g. 192.168.1.255).</summary>
    public static string? GetBroadcastHint()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                var ip = addr.Address.GetAddressBytes();
                var mask = addr.IPv4Mask?.GetAddressBytes();
                if (mask is null) continue;

                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++)
                    broadcast[i] = (byte)(ip[i] | (~mask[i] & 0xFF));

                return string.Join(".", broadcast);
            }
        }
        return null;
    }
}
