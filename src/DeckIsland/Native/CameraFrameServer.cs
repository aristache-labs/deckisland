using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;

namespace DeckIsland.Native;

// Some desktop camera clients start a stream without appearing in the Windows consent store.
// The Frame Server log can report the stream's lifetime, but does not identify the client.
internal sealed class CameraFrameServer
{
    private const string Channel = "Microsoft-Windows-MF-FrameServer/Camera_FrameServer";
    private const string StreamEvents = "*[System[EventID=12 and Version=0] and EventData[Data[@Name='TimerScope']='2' and (Data[@Name='WatchdogOperation']='2' or Data[@Name='WatchdogOperation']='3')]]";
    private const uint Start = 2;
    private long? _newestRecord;
    private List<(int ProcessId, DateTime Started)> _starts = [];
    private bool _unavailable;

    internal bool IsActive()
    {
        if (_unavailable) return false;
        try
        {
            using var reader = new EventLogReader(new EventLogQuery(Channel, PathType.LogName, StreamEvents) { ReverseDirection = true });
            using var newest = reader.ReadEvent();
            if (newest?.RecordId != _newestRecord)
            {
                _starts = ReadStarts(reader, newest);
                _newestRecord = newest?.RecordId;
            }
            return _starts.Any(start => StillRunning(start.ProcessId, start.Started));
        }
        catch (Exception error) when (error is EventLogNotFoundException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _unavailable = true;
            return false;
        }
        catch (EventLogException) { return false; }
    }

    private static List<(int ProcessId, DateTime Started)> ReadStarts(EventLogReader reader, EventRecord? newest)
    {
        var streams = new HashSet<(int ProcessId, ulong Stream)>();
        var starts = new List<(int, DateTime)>();
        Add(newest);
        for (int i = 0; i < 127; i++)
        {
            using var record = reader.ReadEvent();
            if (record is null) break;
            Add(record);
        }
        return starts;

        void Add(EventRecord? record)
        {
            if (record?.ProcessId is not { } pid || record.TimeCreated is not { } time) return;
            var data = record.Properties;
            if (data.Count >= 3 && data[1].Value is ulong stream && data[2].Value is uint operation &&
                streams.Add((pid, stream)) && operation == Start)
                starts.Add((pid, time));
        }
    }

    private static bool StillRunning(int pid, DateTime started)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            try { return process.StartTime <= started; }
            // Frame Server normally runs in a service process. The PID can still be present when opening it is denied.
            catch (Win32Exception error) when (error.NativeErrorCode == 5) { return true; }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }
}
