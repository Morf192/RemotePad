namespace RemotePad;

internal static class MouseButtonMessage
{
    public static bool TryGet(int message, uint data, out Key key, out bool down)
    {
        down = message is 0x201 or 0x204 or 0x207 or 0x20B;
        key = message switch
        {
            0x201 or 0x202 => Key.MouseLeft,
            0x204 or 0x205 => Key.MouseRight,
            0x207 or 0x208 => Key.MouseMiddle,
            0x20B or 0x20C when (data >> 16) == 1 => Key.MouseX1,
            0x20B or 0x20C when (data >> 16) == 2 => Key.MouseX2,
            _ => 0
        };
        return key != 0;
    }
}
