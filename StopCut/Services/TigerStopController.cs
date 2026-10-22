using System.Globalization;
using System.IO.Ports;
using System.Text;
using StopCut.Models;

namespace StopCut.Services;

/// <summary>
/// Talks to a TigerStop Amp 5.x over RS-232 using its ASCII command set (TigerStop RS-232 Comm Spec,
/// github.com/TigerStop/TigerStopSDK/wiki): commands end in CR, "MG&lt;pos&gt;" moves, and the amp
/// answers "MGS &lt;target&gt;" when a move starts, "MGF" when it finishes, and "X EC=n text" on errors.
/// </summary>
public sealed class TigerStopController : IDisposable
{
    public const string HomeCommand = "MH";
    public const string StopCommand = "MS";
    public const string EmergencyStopCommand = "ME";
    public const string PositionQueryCommand = "P";
    public const string VersionCommand = "V";

    /// <summary>The amp's selectable baud rates, its default (57600) first.</summary>
    public static readonly int[] DetectBaudRates = { 57600, 9600, 19200, 38400, 115200, 230400 };

    /// <summary>True if a reply looks like the amp's version banner, e.g. "TigerStop Amp III ver = 5.23".</summary>
    public static bool IsVersionReply(string text) =>
        text.Contains("ver", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("TigerStop", StringComparison.OrdinalIgnoreCase);

    private SerialPort? _port;

    /// <summary>Raised for every line sent (TX) or received (RX). May fire on a background thread.</summary>
    public event Action<string>? Log;

    /// <summary>Raw bytes received from the port. Fires on a background thread.</summary>
    public event Action<byte[]>? Received;

    /// <summary>Raw bytes written to the port.</summary>
    public event Action<byte[]>? Sent;

    public bool IsOpen => _port?.IsOpen == true;

    /// <summary>Pause after each character sent, in milliseconds.</summary>
    public int CharDelayMs { get; set; }

    public void Open(SerialSettings settings)
    {
        Close();
        _port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
        {
            Handshake = settings.Handshake,
            ReadTimeout = 500,
            WriteTimeout = 1000,
            Encoding = Encoding.ASCII,
            DtrEnable = true,
            RtsEnable = true,
        };
        _port.DataReceived += OnDataReceived;
        CharDelayMs = settings.CharDelayMs;
        _port.Open();
        Log?.Invoke($"Opened {settings.PortName} at {settings.BaudRate} baud");
    }

    public void Close()
    {
        if (_port is null) return;
        _port.DataReceived -= OnDataReceived;
        if (_port.IsOpen)
        {
            _port.Close();
            Log?.Invoke("Port closed");
        }
        _port.Dispose();
        _port = null;
    }

    /// <summary>Moves the stop to <paramref name="position"/> using the configured command template.</summary>
    public void MoveTo(double position, SerialSettings settings) => SendRaw(FormatMove(position, settings));

    /// <summary>Sends a protocol command with the configured line ending.</summary>
    public void SendCommand(string command, SerialSettings settings) => SendRaw(command + Unescape(settings.Terminator));

    /// <summary>
    /// Tries every COM port at each of the amp's baud rates and returns the ones where a TigerStop
    /// answers the version query. Blocking; run it off the UI thread with no port open.
    /// </summary>
    public static List<(string Port, int Baud)> FindConnections()
    {
        var found = new List<(string, int)>();
        var query = Encoding.ASCII.GetBytes(VersionCommand + "\r");
        foreach (var name in SerialPort.GetPortNames())
        {
            foreach (var baud in DetectBaudRates)
            {
                try
                {
                    using var port = new SerialPort(name, baud, Parity.None, 8, StopBits.One)
                    {
                        Handshake = Handshake.None,
                        DtrEnable = true,
                        RtsEnable = true,
                        Encoding = Encoding.ASCII,
                    };
                    port.Open();
                    port.DiscardInBuffer();
                    port.Write(query, 0, query.Length);
                    var reply = new StringBuilder();
                    for (var waited = 0; waited < 3000 && !IsVersionReply(reply.ToString()); waited += 100)
                    {
                        Thread.Sleep(100);
                        reply.Append(port.ReadExisting());
                    }
                    if (IsVersionReply(reply.ToString()))
                    {
                        found.Add((name, baud));
                        break;
                    }
                }
                catch
                {
                    // Busy ports and garbage at the wrong baud rate are expected while scanning.
                }
            }
        }
        return found;
    }

    public void SendRaw(string text)
    {
        if (_port is not { IsOpen: true }) throw new InvalidOperationException("The serial port is not open.");
        var bytes = Encoding.ASCII.GetBytes(text);
        if (CharDelayMs <= 0)
        {
            _port.Write(bytes, 0, bytes.Length);
        }
        else
        {
            // Older amps read characters one at a time and can drop a fast burst.
            for (var i = 0; i < bytes.Length; i++)
            {
                _port.Write(bytes, i, 1);
                Thread.Sleep(CharDelayMs);
            }
        }
        Log?.Invoke("TX " + Escape(text));
        Sent?.Invoke(bytes);
    }

    public static string FormatMove(double position, SerialSettings settings)
    {
        var pos = position.ToString("F" + settings.Decimals, CultureInfo.InvariantCulture);
        return settings.MoveCommandTemplate.Replace("{pos}", pos) + Unescape(settings.Terminator);
    }

    public static string Unescape(string s) =>
        s.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");

    /// <summary>Makes control characters visible: \r \n \t, and \xNN for the rest.</summary>
    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case < ' ' or > '~': sb.Append($"\\x{(int)c:X2}"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (_port is not { IsOpen: true } port) return;
            var count = port.BytesToRead;
            if (count <= 0) return;
            var bytes = new byte[count];
            count = port.Read(bytes, 0, count);
            if (count < bytes.Length) Array.Resize(ref bytes, count);
            Received?.Invoke(bytes);
        }
        catch (Exception ex)
        {
            Log?.Invoke("RX error: " + ex.Message);
        }
    }

    public void Dispose() => Close();
}
