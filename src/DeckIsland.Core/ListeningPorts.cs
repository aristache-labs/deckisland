using System.Net;
using System.Net.Sockets;

namespace DeckIsland.Core;

// A port a process listens on, with every local address it listens on there (IPv4 and IPv6 together).
public sealed record ListeningPort(int Port, int ProcessId, IReadOnlyList<IPAddress> Addresses)
{
    // Reachable from other machines: it listens on some address that is not a loopback one (an "any" address included).
    public bool Public => Addresses.Any(address => !IPAddress.IsLoopback(address));
    public string AddressText => string.Join(", ", Addresses.Select(address => address.ToString()));

    // The address a browser on this PC reaches it at: the loopback for an "any" or loopback listener, else the address itself.
    public string Host
    {
        get
        {
            bool v4 = Addresses.Any(a => a.AddressFamily == AddressFamily.InterNetwork && (IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any)));
            if (v4) return "127.0.0.1";
            bool v6 = Addresses.Any(a => a.AddressFamily == AddressFamily.InterNetworkV6 && (IPAddress.IsLoopback(a) || a.Equals(IPAddress.IPv6Any)));
            if (v6) return "[::1]";
            var first = Addresses[0];
            return first.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{first}]" : first.ToString();
        }
    }
    public string Endpoint => $"{Host}:{Port}";
    public string Url => $"http://{Endpoint}";
}

public static class ListeningPorts
{
    // One entry per port and process, IPv4 addresses before IPv6, each address once.
    public static List<ListeningPort> Group(IEnumerable<(IPAddress Address, int Port, int ProcessId)> sockets) =>
        [.. sockets.GroupBy(s => (s.Port, s.ProcessId))
            .Select(group => new ListeningPort(group.Key.Port, group.Key.ProcessId,
                [.. group.Select(s => s.Address).Distinct().OrderBy(a => a.AddressFamily == AddressFamily.InterNetworkV6 ? 1 : 0)]))
            .OrderBy(entry => entry.Port).ThenBy(entry => entry.ProcessId)];
}
