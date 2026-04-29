using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VrcOscSender;

public sealed class OscListener : IDisposable
{
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    private int _listenPort;
    public int ListenPort => _listenPort;

    public event Action<string>? AvatarChanged;
    public event Action<string>? ListenError;

    public OscListener(int listenPort = 9001)
    {
        _listenPort = listenPort;
    }

    /// <summary>Update the port before calling Start().</summary>
    public void UpdatePort(int port) => _listenPort = port;

    public void Start()
    {
        Stop();
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, _listenPort));
        }
        catch (SocketException ex)
        {
            ListenError?.Invoke($"Cannot listen on port {ListenPort}: {ex.Message}");
            return;
        }
        _cts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udp?.Close();
        _udp?.Dispose();
        _udp = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp!.ReceiveAsync(ct);
                ParsePacket(result.Buffer);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException)    { break; }
            catch (Exception ex)
            {
                ListenError?.Invoke($"Receive error: {ex.Message}");
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    private void ParsePacket(byte[] data)
    {
        if (data.Length < 8) return;
        try
        {
            int pos = 0;
            string address = ReadOscString(data, ref pos);
            if (address != "/avatar/change") return;
            string typeTags = ReadOscString(data, ref pos);
            if (!typeTags.Contains('s')) return;
            string avatarId = ReadOscString(data, ref pos);
            if (!string.IsNullOrWhiteSpace(avatarId))
                AvatarChanged?.Invoke(avatarId);
        }
        catch { /* drop malformed packets */ }
    }

    private static string ReadOscString(byte[] data, ref int pos)
    {
        int start = pos;
        while (pos < data.Length && data[pos] != 0) pos++;
        string s = Encoding.ASCII.GetString(data, start, pos - start);
        pos++;
        int mod = pos % 4;
        if (mod != 0) pos += 4 - mod;
        return s;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
