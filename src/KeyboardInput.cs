#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RemotePad;

[StructLayout(LayoutKind.Sequential)]
internal struct PadState
{
    public ushort Buttons;
    public byte LT, RT;
    public short LX, LY, RX, RY;
}

internal enum Key
{
    MouseLeft = 0x01, MouseRight = 0x02, MouseMiddle = 0x04, MouseX1 = 0x05, MouseX2 = 0x06,
    Tab = 0x09, Enter = 0x0D, Shift = 0x10, Control = 0x11, Space = 0x20,
    Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28,
    A = 0x41, C = 0x43, D = 0x44, E = 0x45, F = 0x46,
    I = 0x49, J = 0x4A, K = 0x4B, L = 0x4C, Q = 0x51, R = 0x52,
    S = 0x53, W = 0x57, X = 0x58, Z = 0x5A, F8 = 0x77,
    LeftShift = 0xA0, RightShift = 0xA1, LeftControl = 0xA2, RightControl = 0xA3
}

internal sealed class KeyboardInput
{
    private static readonly (PadControl Control, ushort Mask)[] Buttons =
    {
        (PadControl.DpadUp, 0x0001), (PadControl.DpadDown, 0x0002),
        (PadControl.DpadLeft, 0x0004), (PadControl.DpadRight, 0x0008),
        (PadControl.Menu, 0x0010), (PadControl.View, 0x0020), (PadControl.Guide, 0x0400),
        (PadControl.L3, 0x0040), (PadControl.R3, 0x0080), (PadControl.LB, 0x0100), (PadControl.RB, 0x0200),
        (PadControl.A, 0x1000), (PadControl.B, 0x2000), (PadControl.X, 0x4000), (PadControl.Y, 0x8000)
    };

    private readonly HashSet<Key> physicallyHeld = new();
    private readonly HashSet<Key> captured = new();
    private readonly HashSet<Key> gamepadHeld = new();
    private readonly Queue<PadState> pending = new();
    private readonly HashSet<Key> pauseHeld = new();
    private readonly MouseAim mouse = new();
    private bool editing;

    public KeyboardInput(ControlSettings settings) => Settings = settings;

    public ControlSettings Settings { get; private set; }
    public bool Active { get; private set; }
    public bool MouseMoving => mouse.Moving;
    public event Action? ReportAvailable;

    public bool OnKey(Key key, bool down)
    {
        if (editing || !Settings.Layout.TryGetAction(key, out PadControl control)) return false;
        if (control == PadControl.Pause)
        {
            if (down)
            {
                if (pauseHeld.Add(key) && pauseHeld.Count == 1) Toggle();
            }
            else pauseHeld.Remove(key);
            return true;
        }

        if (!down)
        {
            physicallyHeld.Remove(key);
            if (!captured.Remove(key)) return false;
            if (gamepadHeld.Remove(key)) QueueReport(Read());
            return true;
        }

        if (!physicallyHeld.Add(key)) return captured.Contains(key);
        if (!Active) return false;
        captured.Add(key);
        gamepadHeld.Add(key);
        QueueReport(Read());
        return true;
    }

    public void Toggle()
    {
        Active = !Active;
        gamepadHeld.Clear();
        mouse.Clear();
        pending.Clear();
        QueueReport(default);
    }

    public void BeginEditing()
    {
        editing = true;
        if (Active) Toggle();
        physicallyHeld.Clear();
        captured.Clear();
        pauseHeld.Clear();
    }

    public void EndEditing(ControlSettings settings)
    {
        Settings = settings;
        editing = false;
    }

    public void OnMouseMove(int dx, int dy, long timestamp)
    {
        if (Active && Settings.MouseCamera && mouse.Move(dx, dy, timestamp, Settings))
            QueueReport(Read());
    }

    public void ExpireMouse(long timestamp)
    {
        if (mouse.Expire(timestamp)) QueueReport(Read());
    }

    private void QueueReport(PadState state)
    {
        pending.Enqueue(state);
        ReportAvailable?.Invoke();
    }

    public bool TryReadReport(out PadState state) => pending.TryDequeue(out state);

    private static void Stick(bool left, bool right, bool down, bool up, out short x, out short y)
    {
        int horizontal = (right ? 1 : 0) - (left ? 1 : 0);
        int vertical = (up ? 1 : 0) - (down ? 1 : 0);
        int scale = horizontal != 0 && vertical != 0 ? 23170 : 32767;
        x = (short)(horizontal * scale);
        y = (short)(vertical * scale);
    }

    public PadState Read()
    {
        var state = new PadState();
        uint controls = 0;
        foreach (Key key in gamepadHeld)
            if (Settings.Layout.TryGetAction(key, out PadControl control)) controls |= 1u << (int)control;
        bool Held(PadControl control) => (controls & (1u << (int)control)) != 0;
        foreach (var (control, mask) in Buttons)
            if (Held(control)) state.Buttons |= mask;

        if (Held(PadControl.LT)) state.LT = 255;
        if (Held(PadControl.RT)) state.RT = 255;
        Stick(Held(PadControl.MoveLeft), Held(PadControl.MoveRight), Held(PadControl.MoveDown),
            Held(PadControl.MoveUp), out state.LX, out state.LY);
        Stick(Held(PadControl.LookLeft), Held(PadControl.LookRight), Held(PadControl.LookDown),
            Held(PadControl.LookUp), out state.RX, out state.RY);
        if (state.RX == 0) state.RX = mouse.X;
        if (state.RY == 0) state.RY = mouse.Y;
        return state;
    }
}

internal sealed class MouseAim
{
    public const int IdleMilliseconds = 24;
    private static readonly long IdleTicks = Stopwatch.Frequency * IdleMilliseconds / 1000;
    private static readonly long AccumulationTicks = Stopwatch.Frequency / 100;
    private long windowStarted, lastMovement;
    private long sumX, sumY;

    public short X { get; private set; }
    public short Y { get; private set; }
    public bool Moving => X != 0 || Y != 0;

    public bool Move(int dx, int dy, long timestamp, ControlSettings settings)
    {
        if (dx == 0 && dy == 0) return false;
        if (!Moving || timestamp - windowStarted >= AccumulationTicks)
        {
            windowStarted = timestamp;
            sumX = sumY = 0;
        }
        // Discard accumulated movement on direction reversal so it reacts immediately.
        if (dx != 0 && Math.Sign(dx) != Math.Sign(sumX)) sumX = 0;
        if (dy != 0 && Math.Sign(dy) != Math.Sign(sumY)) sumY = 0;
        sumX = Math.Clamp(sumX + dx, -32767, 32767);
        sumY = Math.Clamp(sumY + dy, -32767, 32767);
        lastMovement = timestamp;
        double length = Math.Sqrt((double)sumX * sumX + (double)sumY * sumY);
        double scale = length == 0 ? 0 : Math.Min(32767, 8000 + length * 1450 * settings.Sensitivity / 100) / length;
        short x = (short)Math.Round(sumX * scale);
        short y = (short)Math.Round((settings.InvertY ? sumY : -sumY) * scale);
        bool changed = x != X || y != Y;
        (X, Y) = (x, y);
        return changed;
    }

    public bool Expire(long timestamp)
    {
        if (!Moving || timestamp - lastMovement < IdleTicks) return false;
        Clear();
        return true;
    }

    public void Clear()
    {
        X = Y = 0;
        sumX = sumY = 0;
    }
}
