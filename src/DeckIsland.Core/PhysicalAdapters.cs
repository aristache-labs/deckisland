namespace DeckIsland.Core;

// A network adapter as the system monitor's Net row judges it: what Windows says about it, and its first IPv4 address.
public sealed record AdapterInfo(string Name, string Description, bool Wireless, bool Ethernet, bool Up, bool HasGateway, string? Address);

// The adapter the Net row names: the physical Wi-Fi or Ethernet one that is connected (up, with a default gateway and an address),
// never a virtual one (WSL's and Hyper-V's vEthernet, VPNs, VirtualBox, VMware, tunnels), which also report themselves as Ethernet.
// Ethernet is preferred over Wi-Fi when both are connected, as Windows routes through the cable then.
public static class PhysicalAdapters
{
    private static readonly string[] Virtual =
    [
        "vethernet", "hyper-v", "virtual", "wsl", "vpn", "tap-", "tap ", "tun", "wireguard", "wintun", "vmware", "virtualbox", "loopback",
        "npcap", "pangp", "anyconnect", "fortinet", "forticlient", "openvpn", "zerotier", "tailscale", "nordlynx", "bluetooth", "teredo", "isatap"
    ];

    public static bool IsVirtual(AdapterInfo adapter) =>
        Virtual.Any(word => adapter.Name.Contains(word, StringComparison.OrdinalIgnoreCase) || adapter.Description.Contains(word, StringComparison.OrdinalIgnoreCase));

    public static AdapterInfo? Choose(IEnumerable<AdapterInfo> adapters) =>
        adapters.Where(adapter => adapter.Up && adapter.HasGateway && adapter.Address is { Length: > 0 } && (adapter.Wireless || adapter.Ethernet) && !IsVirtual(adapter))
            .OrderBy(adapter => adapter.Ethernet ? 0 : 1).FirstOrDefault();
}
