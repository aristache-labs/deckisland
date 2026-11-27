namespace DeckIsland.Core;

// What a jump list is filed under. Windows keeps an app's jump list in %APPDATA%\Microsoft\Windows\Recent\*Destinations\<id>.*,
// the id being a CRC-64 (reflected ECMA-182, all ones at the start, no final xor) of the app's AppUserModelID in upper case, as
// UTF-16. An app with no AppUserModelID of its own has an implicit one: its exe's path with the known folder it is in written as
// that folder's id.
public static class DockJumpList
{
    private const ulong Polynomial = 0x92C64265D32139A4;
    private static readonly ulong[] Table = BuildTable();

    private static ulong[] BuildTable()
    {
        var table = new ulong[256];
        for (ulong i = 0; i < 256; i++)
        {
            ulong crc = i;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? crc >> 1 ^ Polynomial : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    // The 16 hex digits Windows names the app's jump list files with.
    public static string FileId(string appId)
    {
        ulong crc = ulong.MaxValue;
        foreach (char c in appId.ToUpperInvariant())
        {
            crc = Table[(byte)(crc ^ (byte)c)] ^ crc >> 8;
            crc = Table[(byte)(crc ^ (byte)(c >> 8))] ^ crc >> 8;
        }
        return crc.ToString("x16");
    }

    // The implicit AppUserModelID of an exe: the known folder at the start of its path written as the folder's id, the longest
    // folder winning. folders pairs each known folder's path with its id.
    public static string ImplicitAppId(string exe, IEnumerable<(string Path, string Id)> folders)
    {
        foreach (var (path, id) in folders.Where(f => f.Path.Length > 0).OrderByDescending(f => f.Path.Length))
        {
            var prefix = path.TrimEnd('\\') + "\\";
            if (exe.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return id + "\\" + exe[prefix.Length..];
        }
        return exe;
    }
}
