using System.Buffers.Binary;
using DeckIsland.Native;
using NAudio.Wave;

internal static class CallMicMeterChecks
{
    internal static void Run(Action<string, bool> check)
    {
        var pcm = new CallMicMeter(new WaveFormat(48000, 16, 1));
        var packet = new byte[4096];
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(2048), -16384);
        pcm.AddSamples(packet, packet.Length);
        pcm.AddSamples(new byte[2], 2);
        check("call mic measures a negative PCM sample from a later chunk and holds it across quiet packets",
            pcm.TryTakePeak(out float pcmPeak) && pcmPeak == .5f);
        check("call mic resets between refreshes and distinguishes a quiet packet from no packet", !pcm.TryTakePeak(out _));
        pcm.AddSamples(new byte[2], 2);
        check("call mic reports an actual packet of silence", pcm.TryTakePeak(out float silentPeak) && silentPeak == 0);

        var ieeeFloat = new CallMicMeter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var frame = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(frame, float.NaN);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(4), -.25f);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(8), 1f);
        ieeeFloat.AddSamples(frame, 8);
        check("call mic reads both float channels and ignores invalid or unrecorded samples",
            ieeeFloat.TryTakePeak(out float floatPeak) && floatPeak == .25f);

        var response = new CallMicResponse();
        response.Update(.025f);
        check("the mic bar and speech state react to the first voiced packet", response.Level > .30 && response.Hearing);
        response.Update(0);
        response.Update(0);
        response.Update(0);
        check("the mic bar decays and the speech state clears within three quiet updates", response.Level < .10 && !response.Hearing);
        response.Update(.04f);
        check("the next syllable lights the mic immediately", response.Level > .39 && response.Hearing);

        check("calls prefer Teams over the screen recorder when both use the microphone",
            CallAudio.MeetingOwner(["SnippingTool", "ms-teams", "MSTeams_123"], [], "SnippingTool") == "Microsoft Teams");
        check("calls keep the current app stable between equally ranked input sessions",
            CallAudio.MeetingOwner(["Zoom", "Webex", "SnippingTool"], [], "Webex") == "Webex");
        check("calls filter the app's own microphone test and ignored input clients",
            CallAudio.MeetingOwner(["DeckIsland", "DeckIsland.Notifications_x820ww3096tjp", "ms-teams", "SnippingTool"],
                ["Microsoft Teams", "SnippingTool.exe"], "") is null);
        check("calls display the Snipping Tool package as its application name",
            CallAudio.MeetingOwner(["Microsoft.ScreenSketch_123"], [], "") == "SnippingTool");
    }
}
