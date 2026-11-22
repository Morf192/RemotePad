using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RemotePad;

internal static class RawMouseInput
{
    public const int MessageId = 0x00FF;

    [StructLayout(LayoutKind.Sequential)]
    private struct Device
    {
        public ushort UsagePage, Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Header
    {
        public uint Type, Size;
        public IntPtr Device, WParam;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Mouse
    {
        [FieldOffset(0)] public ushort Flags;
        [FieldOffset(12)] public int X;
        [FieldOffset(16)] public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Packet
    {
        public Header Header;
        public Mouse Mouse;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(ref Device device, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr input, uint command, out Packet data, ref uint size, uint headerSize);

    private static readonly uint DeviceSize = (uint)Marshal.SizeOf<Device>();
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<Header>();
    private static readonly uint PacketSize = (uint)Marshal.SizeOf<Packet>();

    public static void Register(IntPtr window)
    {
        var device = new Device { UsagePage = 0x01, Usage = 0x02, Flags = 0x100, Target = window };
        if (!RegisterRawInputDevices(ref device, 1, DeviceSize))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static void Unregister()
    {
        var device = new Device { UsagePage = 0x01, Usage = 0x02, Flags = 0x01 };
        RegisterRawInputDevices(ref device, 1, DeviceSize);
    }

    public static bool TryRead(IntPtr input, out int dx, out int dy)
    {
        uint size = PacketSize;
        uint read = GetRawInputData(input, 0x10000003, out Packet packet, ref size, HeaderSize);
        dx = dy = 0;
        if (read != PacketSize || packet.Header.Type != 0 || (packet.Mouse.Flags & 1) != 0) return false;
        (dx, dy) = (packet.Mouse.X, packet.Mouse.Y);
        return dx != 0 || dy != 0;
    }
}
