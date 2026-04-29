using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VrcOscSender;

public sealed class OscClient : IDisposable
{
    private readonly UdpClient _udp;
    private bool _disposed;

    public OscClient(string host, int port)
    {
        _udp = new UdpClient();
        _udp.Connect(new IPEndPoint(IPAddress.Parse(host), port));
    }

    public void SendBool(string address, bool value)
        => Send(BuildBoolMessage(address, value));

    public void SendInt(string address, int value)
        => Send(BuildMessage(address, value, 'i'));

    public void SendFloat(string address, float value)
        => Send(BuildMessage(address, value, 'f'));

    // Booleans in OSC use type tags T or F with no value bytes
    private static byte[] BuildBoolMessage(string address, bool value)
    {
        using var ms = new MemoryStream();
        WriteOscString(ms, address);
        WriteOscString(ms, value ? ",T" : ",F");
        return ms.ToArray();
    }

    private static byte[] BuildMessage(string address, object value, char typeTag)
    {
        using var ms = new MemoryStream();
        WriteOscString(ms, address);
        WriteOscString(ms, "," + typeTag);
        if (typeTag == 'i') WriteInt32(ms, (int)value);
        if (typeTag == 'f') WriteFloat(ms, (float)value);
        return ms.ToArray();
    }

    private static void WriteOscString(MemoryStream ms, string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        ms.Write(bytes, 0, bytes.Length);
        ms.WriteByte(0);
        int total = bytes.Length + 1;
        int pad = (4 - (total % 4)) % 4;
        for (int i = 0; i < pad; i++) ms.WriteByte(0);
    }

    private static void WriteInt32(MemoryStream ms, int value)
    {
        var b = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        ms.Write(b, 0, 4);
    }

    private static void WriteFloat(MemoryStream ms, float value)
    {
        var b = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        ms.Write(b, 0, 4);
    }

    private void Send(byte[] packet) => _udp.Send(packet, packet.Length);

    public void Dispose()
    {
        if (_disposed) return;
        _udp.Dispose();
        _disposed = true;
    }
}
