namespace F1Controller.Pad;

/// <summary>Builds DS4 output reports.</summary>
public static class Ds4Output
{
    public const int UsbLength = 32;

    /// <summary>
    /// USB output report 0x05 with only the rumble flag set, so the lightbar is left alone.
    /// </summary>
    /// <param name="heavy">Left (low-frequency) motor, 0..255.</param>
    /// <param name="light">Right (high-frequency) motor, 0..255.</param>
    public static byte[] UsbRumble(byte heavy, byte light)
    {
        var b = new byte[UsbLength];
        b[0] = 0x05;
        b[1] = 0x01; // flags: rumble only
        b[4] = light;
        b[5] = heavy;
        return b;
    }
}
