using System.IO.Ports;

namespace StopCut.Models;

/// <summary>RS-232 port settings and the command format the TigerStop expects.</summary>
public sealed class SerialSettings
{
    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 57600;
    public int DataBits { get; set; } = 8;
    public Parity Parity { get; set; } = Parity.None;
    public StopBits StopBits { get; set; } = StopBits.One;
    public Handshake Handshake { get; set; } = Handshake.None;

    /// <summary>
    /// Text sent to move the stop; {pos} is replaced with the position.
    /// "MG{pos}" is the TigerStop move-go command.
    /// </summary>
    public string MoveCommandTemplate { get; set; } = DefaultMoveCommand;

    /// <summary>Line ending appended to every command. Escapes \r \n \t are allowed. The amp expects CR.</summary>
    public string Terminator { get; set; } = DefaultTerminator;

    public const string DefaultMoveCommand = "MG{pos}";
    public const string DefaultTerminator = "\\r";

    /// <summary>Decimal places used when formatting the position.</summary>
    public int Decimals { get; set; } = 3;

    /// <summary>Pause after each character sent (ms). Slow amps can drop characters sent in one burst.</summary>
    public int CharDelayMs { get; set; } = 5;

    /// <summary>Units the amp works in (its MMEnable setting, D44). Read from the amp on connect.</summary>
    public LengthUnit MachineUnits { get; set; } = LengthUnit.Inches;

    /// <summary>Added to every part length before sending (blade/stop calibration).</summary>
    public double PositionOffset { get; set; }
}
