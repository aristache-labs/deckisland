using System.Net;
using DeckIsland.Core;

static class ListeningPortsTests
{
    public static void Run(Action<string, bool> check)
    {
        var ports = ListeningPorts.Group([
            (IPAddress.IPv6Loopback, 3000, 10), (IPAddress.Loopback, 3000, 10), (IPAddress.Loopback, 3000, 10),
            (IPAddress.Any, 445, 4), (IPAddress.IPv6Any, 445, 4),
            (IPAddress.IPv6Loopback, 5173, 20), (IPAddress.Parse("192.168.1.20"), 9000, 30)]);
        check("Listening ports: one entry per port and process, by port, IPv4 first, each address once",
            ports.Select(p => p.Port).SequenceEqual([445, 3000, 5173, 9000]) && ports[1].AddressText == "127.0.0.1, ::1");
        check("Listening ports: an any or other address is public, loopback only is not",
            ports[0].Public && !ports[1].Public && !ports[2].Public && ports[3].Public);
        check("Listening ports: the browser goes to the loopback, IPv6 in brackets, or the address itself",
            ports[0].Url == "http://127.0.0.1:445" && ports[1].Endpoint == "127.0.0.1:3000" && ports[2].Endpoint == "[::1]:5173" && ports[3].Endpoint == "192.168.1.20:9000");
    }
}
