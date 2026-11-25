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
        AdapterInfo Make(string name, string description, bool wireless = false, bool ethernet = true, bool up = true, bool gateway = true, string? address = "192.168.1.5")
            => new(name, description, wireless, ethernet, up, gateway, address);
        var wsl = Make("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", address: "172.23.96.1");
        var vpn = Make("VPN", "PANGP Virtual Ethernet Adapter", address: "10.0.0.2");
        var wifi = Make("Wi-Fi", "Intel(R) Wi-Fi 6E AX211", wireless: true, ethernet: false, address: "192.168.1.104");
        var cable = Make("Ethernet", "Realtek PCIe GbE", address: "192.168.1.20");
        check("Net address: WSL's and a VPN's adapters are passed over for the Wi-Fi", PhysicalAdapters.Choose([wsl, vpn, wifi]) == wifi);
        check("Net address: a connected cable wins over the Wi-Fi", PhysicalAdapters.Choose([wifi, cable]) == cable);
        check("Net address: an adapter that is down or has no gateway is not the connected one",
            PhysicalAdapters.Choose([cable with { Up = false }, cable with { HasGateway = false }, wifi]) == wifi && PhysicalAdapters.Choose([wsl]) is null);
    }
}
