using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using DeckIsland.Core;
using DeckIsland.Core.ClipboardSync;

namespace DeckIsland;

// A phone without an app: a small web server on this PC's local network. A page (opened from a QR code) sends text and pictures to this
// clipboard and shows the last text copied here; /clip and /image do the same for an iOS Shortcut or any HTTP client. Every request must
// carry the secret in the link (k=...), and only addresses of the local network are served. It is plain HTTP (an iPhone does not trust a
// certificate made by the PC), so the link is for a network the user trusts, and "New link" ends the old one.
internal sealed partial class ClipboardSyncService
{
    public const int PhonePort = 47835;
    private const int PhoneMaxBody = 20 * 1024 * 1024, PhoneImageSide = 2048;
    private CancellationTokenSource? _phoneStop;
    private string _phoneLatest = "";
    public string PhoneStatus { get; private set; } = "";
    public SyncLevel PhoneLevel { get; private set; }

    // The link for the phone: this PC's first address on a private network. Empty when there is none.
    public string PhoneUrl()
    {
        var host = LanNetwork.Interfaces().FirstOrDefault();
        return host is null || _app.Settings.PhoneSyncToken.Length == 0 ? "" : $"http://{host.Address}:{PhonePort}/?k={_app.Settings.PhoneSyncToken}";
    }

    private void ApplyPhone()
    {
        if (!_app.Settings.PhoneClipboardSync || _app.Settings.PhoneSyncToken.Length == 0) { StopPhone(); return; }
        if (_phoneStop is not null) return;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(_app.Lifetime.Token);
        _phoneStop = stop;
        _ = RunPhoneAsync(stop);
    }

    private void StopPhone()
    {
        var stop = _phoneStop;
        _phoneStop = null;
        PhoneStatus = ""; PhoneLevel = SyncLevel.Idle;
        stop?.Cancel();
    }

    private async Task RunPhoneAsync(CancellationTokenSource stop)
    {
        void Status(string message, SyncLevel level)
        {
            if (_disposed || !ReferenceEquals(_phoneStop, stop)) return;
            PhoneStatus = message; PhoneLevel = level;
            Changed?.Invoke();
        }
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, PhonePort);
            listener.Start();
            Status(Loc.T("等待手机连接", "Waiting for the phone"), SyncLevel.Active);
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                _ = Task.Run(() => HandlePhoneAsync(client, stop.Token));
            }
        }
        catch (SocketException ex)
        {
            Log("phone server: " + Describe(ex));
            Status(Loc.T("无法启动：端口被占用或被防火墙阻止", "Cannot start: the port is in use or blocked"), SyncLevel.Attention);
        }
        catch (OperationCanceledException) { }
        finally { listener?.Stop(); }
    }

    private static bool PhoneAllowed(IPAddress remote)
    {
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (IPAddress.IsLoopback(remote)) return true;
        if (remote.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = remote.GetAddressBytes();
        return b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31;
    }

    private async Task HandlePhoneAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            if ((client.Client.RemoteEndPoint as IPEndPoint)?.Address is not { } remote || !PhoneAllowed(remote)) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var stream = client.GetStream();
            var buffer = new byte[16384];
            int filled = 0, headerEnd = -1;
            while (headerEnd < 0)
            {
                if (filled == buffer.Length) { await PhoneReplyAsync(stream, 431, "text/plain", "Header too large", timeout.Token); return; }
                int read = await stream.ReadAsync(buffer.AsMemory(filled), timeout.Token);
                if (read == 0) return;
                filled += read;
                headerEnd = buffer.AsSpan(0, filled).IndexOf("\r\n\r\n"u8);
            }
            var lines = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
            var request = lines[0].Split(' ');
            if (request.Length < 2) return;
            long length = 0; string contentType = "";
            foreach (var line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string name = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) long.TryParse(value, out length);
                else if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = value.ToLowerInvariant();
            }
            if (length is < 0 or > PhoneMaxBody) { await PhoneReplyAsync(stream, 413, "text/plain", "Too large", timeout.Token); return; }
            var body = new byte[length];
            int have = Math.Min((int)length, filled - headerEnd - 4);
            Array.Copy(buffer, headerEnd + 4, body, 0, have);
            while (have < length)
            {
                int read = await stream.ReadAsync(body.AsMemory(have), timeout.Token);
                if (read == 0) return;
                have += read;
            }

            string target = request[1], path = target, query = "";
            int mark = target.IndexOf('?');
            if (mark >= 0) { path = target[..mark]; query = target[(mark + 1)..]; }
            string key = query.Split('&').Select(part => part.Split('=', 2)).Where(part => part.Length == 2 && part[0] == "k").Select(part => Uri.UnescapeDataString(part[1])).FirstOrDefault() ?? "";
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(_app.Settings.PhoneSyncToken)))
            {
                await Task.Delay(500, timeout.Token);
                await PhoneReplyAsync(stream, 403, "text/plain", "Forbidden", timeout.Token);
                return;
            }
            string method = request[0].ToUpperInvariant();
            if (method == "GET" && path == "/") await PhoneReplyAsync(stream, 200, "text/html; charset=utf-8", PhonePage(), timeout.Token);
            else if (method == "GET" && path == "/clip") await PhoneReplyAsync(stream, 200, "text/plain; charset=utf-8", _phoneLatest, timeout.Token);
            else if (method == "POST" && path == "/clip") await PhoneReplyAsync(stream, PhoneText(body, contentType) ? 200 : 400, "text/plain", "", timeout.Token);
            else if (method == "POST" && path == "/image") await PhoneReplyAsync(stream, await PhoneImageAsync(body) ? 200 : 400, "text/plain", "", timeout.Token);
            else await PhoneReplyAsync(stream, 404, "text/plain", "Not found", timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
    }

    private static async Task PhoneReplyAsync(Stream stream, int status, string type, string text, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(text);
        string reason = status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 413 => "Payload Too Large", _ => "Error" };
        string head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n"
            + "Referrer-Policy: no-referrer\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    // The text may come as the raw body, as {"text": "..."} or as a form field "text", whichever the sender can make.
    private bool PhoneText(byte[] body, string contentType)
    {
        string text;
        try
        {
            string raw = Encoding.UTF8.GetString(body);
            if (contentType.StartsWith("application/json")) text = JsonDocument.Parse(raw).RootElement.GetProperty("text").GetString() ?? "";
            else if (contentType.StartsWith("application/x-www-form-urlencoded"))
                text = raw.Split('&').Select(part => part.Split('=', 2)).Where(part => part.Length == 2 && part[0] == "text").Select(part => Uri.UnescapeDataString(part[1].Replace('+', ' '))).FirstOrDefault() ?? "";
            else text = raw;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException) { return false; }
        if (text.Length == 0 || Encoding.UTF8.GetByteCount(text) > SyncTransport.MaxTextBytes) return false;
        string source = Loc.T("手机", "Phone");
        _app.Dispatcher.BeginInvoke(() => AcceptText(text, source));
        return true;
    }

    private async Task<bool> PhoneImageAsync(byte[] bytes)
    {
        try
        {
            var png = await Task.Run(() =>
            {
                using var input = new MemoryStream(bytes);
                BitmapSource image = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                double scale = Math.Min(1, (double)PhoneImageSide / Math.Max(image.PixelWidth, image.PixelHeight));
                if (scale < 1) image = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = new MemoryStream();
                encoder.Save(output);
                return LanPng.Validate(output.ToArray());
            });
            string source = Loc.T("手机", "Phone");
            await _app.Dispatcher.InvokeAsync(() =>
            {
                using var input = new MemoryStream(png.Bytes);
                var decoded = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                decoded.Freeze();
                AcceptImage(png, decoded, source);
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or FileFormatException or ArgumentException) { return false; }
    }

    private static string PhonePage()
    {
        string H(string zh, string en) => System.Net.WebUtility.HtmlEncode(Loc.T(zh, en));
        string J(string zh, string en) => Loc.T(zh, en).Replace("\\", "\\\\").Replace("'", "\\'");
        return """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>DeckIsland</title>
<style>:root{color-scheme:light dark;--bg:#fff;--fg:#111;--card:#f2f2f4;--ac:#0a84ff}@media(prefers-color-scheme:dark){:root{--bg:#000;--fg:#f2f2f2;--card:#1c1c1e}}
body{margin:0;padding:20px 16px 40px;background:var(--bg);color:var(--fg);font:16px -apple-system,system-ui,sans-serif;max-width:560px;margin:auto}
h1{font-size:22px;margin:4px 0 16px}h2{font-size:13px;font-weight:600;opacity:.6;margin:22px 0 8px;text-transform:uppercase;letter-spacing:.04em}
textarea,pre{box-sizing:border-box;width:100%;min-height:110px;margin:0;padding:12px;border:0;border-radius:12px;background:var(--card);color:var(--fg);font:inherit;white-space:pre-wrap;word-break:break-word;resize:vertical}
button,label.b{display:inline-block;margin:10px 8px 0 0;padding:10px 18px;border:0;border-radius:20px;background:var(--ac);color:#fff;font:inherit;font-weight:600}
label.b{background:var(--card);color:var(--fg)}p{min-height:1.2em;opacity:.7;margin:8px 0 0}</style></head><body>
<h1>DeckIsland</h1>
<h2>@TO@</h2><textarea id="t"></textarea><button id="s">@SEND@</button><label class="b">@IMG@<input id="f" type="file" accept="image/*" hidden></label><p id="m"></p>
<h2>@FROM@</h2><pre id="r"></pre><button id="c">@COPY@</button>
<script>
const k=new URLSearchParams(location.search).get('k')||'',q='?k='+encodeURIComponent(k),$=i=>document.getElementById(i);
const say=x=>{$('m').textContent=x;setTimeout(()=>{if($('m').textContent===x)$('m').textContent=''},2500)};
async function post(path,body,type){try{const r=await fetch(path+q,{method:'POST',headers:{'Content-Type':type},body});say(r.ok?'@OK@':'@FAIL@');return r.ok}catch(e){say('@FAIL@');return false}}
$('s').onclick=async()=>{const v=$('t').value;if(v&&await post('/clip',v,'text/plain;charset=utf-8'))$('t').value=''};
$('f').onchange=async e=>{const f=e.target.files[0];if(f)await post('/image',f,f.type||'image/jpeg');e.target.value=''};
$('c').onclick=()=>{const a=document.createElement('textarea');a.value=$('r').textContent;document.body.appendChild(a);a.select();a.setSelectionRange(0,1e9);document.execCommand('copy');a.remove();say('@COPIED@')};
async function poll(){try{const r=await fetch('/clip'+q);if(r.ok){const v=await r.text();if(v!==$('r').textContent)$('r').textContent=v}}catch(e){}setTimeout(poll,3000)}poll();
</script></body></html>
"""
            .Replace("@TO@", H("发送到电脑", "Send to the PC")).Replace("@SEND@", H("发送", "Send")).Replace("@IMG@", H("图片", "Picture"))
            .Replace("@FROM@", H("电脑上最近复制的文字", "Last text copied on the PC")).Replace("@COPY@", H("复制", "Copy"))
            .Replace("@OK@", J("已发送", "Sent")).Replace("@FAIL@", J("发送失败", "Not sent")).Replace("@COPIED@", J("已复制", "Copied"));
    }
}
