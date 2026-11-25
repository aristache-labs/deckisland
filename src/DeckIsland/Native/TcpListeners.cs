using System.Net;
using System.Runtime.InteropServices;

namespace DeckIsland.Native;

// IPv4 listening sockets with their owning process, read from the system table without starting netstat.
internal static class TcpListeners
{
    public readonly record struct Listener(IPAddress Address, int Port, int ProcessId);

    public static List<Listener> Read()
    {
        var listeners = new List<Listener>();
        nint table = 0;
        try
        {
            int size = 0;
            GetExtendedTcpTable(0, ref size, false, 2, 3, 0); // IPv4, TCP_TABLE_OWNER_PID_LISTENER
            if (size <= 0 || size > 16_777_216) return listeners;
            table = Marshal.AllocHGlobal(size);
            if (GetExtendedTcpTable(table, ref size, false, 2, 3, 0) != 0) return listeners;
            int count = Marshal.ReadInt32(table), rowSize = Marshal.SizeOf<Row>();
            if (count < 0 || count > (size - sizeof(int)) / rowSize) return listeners;
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<Row>(table + sizeof(int) + i * rowSize);
                int port = (int)((row.LocalPort & 255) << 8 | (row.LocalPort >> 8 & 255));
                listeners.Add(new(new IPAddress(row.LocalAddress), port, (int)row.Pid));
            }
            return listeners;
        }
        finally { if (table != 0) Marshal.FreeHGlobal(table); }
    }

    // IPv4 and IPv6 listening sockets together, for the listening ports page.
    public static List<Listener> ReadAll()
    {
        var listeners = Read();
        nint table = 0;
        try
        {
            int size = 0;
            GetExtendedTcpTable(0, ref size, false, 23, 3, 0); // IPv6, TCP_TABLE_OWNER_PID_LISTENER
            if (size <= 0 || size > 16_777_216) return listeners;
            table = Marshal.AllocHGlobal(size);
            if (GetExtendedTcpTable(table, ref size, false, 23, 3, 0) != 0) return listeners;
            int count = Marshal.ReadInt32(table), rowSize = Marshal.SizeOf<Row6>();
            if (count < 0 || count > (size - sizeof(int)) / rowSize) return listeners;
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<Row6>(table + sizeof(int) + i * rowSize);
                int port = (int)((row.LocalPort & 255) << 8 | (row.LocalPort >> 8 & 255));
                listeners.Add(new(new IPAddress(row.LocalAddress, row.LocalScopeId), port, (int)row.Pid));
            }
            return listeners;
        }
        finally { if (table != 0) Marshal.FreeHGlobal(table); }
    }

    // MIB_TCP6ROW_OWNER_PID.
    [StructLayout(LayoutKind.Sequential)]
    private struct Row6
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
        public uint LocalScopeId, LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddress;
        public uint RemoteScopeId, RemotePort, State, Pid;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Row { public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, Pid; }
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(nint table, ref int size, bool order, int family, int tableClass, uint reserved);
}
