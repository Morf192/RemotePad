using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RemotePad;

internal static class DriverTest
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint Packet;
        public PadState Pad;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint slot, out XInputState state);

    private static bool WaitFor(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 8000)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    public static void Run(Action<string> log)
    {
        var occupied = new bool[4];
        for (uint i = 0; i < 4; i++) occupied[i] = XInputGetState(i, out _) == 0;
        uint slot = 4;
        var expected = new PadState { Buttons = 0x9040, LT = 255, RT = 255,
            LX = 32767, RX = -23170, RY = -23170 };
        using (var pad = new VirtualPad())
        {
            SelfTest.Require(WaitFor(() =>
            {
                for (uint i = 0; i < 4; i++)
                {
                    if (occupied[i] || XInputGetState(i, out _) != 0) continue;
                    slot = i;
                    return true;
                }
                return false;
            }), "Windows XInput detected a new controller", log);
            pad.Send(expected);
            SelfTest.Require(WaitFor(() => XInputGetState(slot, out var state) == 0 && state.Pad.Equals(expected)),
                "Windows XInput read both sticks, buttons and triggers", log);
            pad.Send(default);
            SelfTest.Require(WaitFor(() => XInputGetState(slot, out var state) == 0 && state.Pad.Equals(default(PadState))),
                "Windows XInput read release to neutral", log);

            var input = new KeyboardInput(ControlSettings.Default());
            using var window = new PadWindow(pad, input);
            RawMouseInput.Register(window.Handle);
            RawMouseInput.Unregister();
            log("PASS Windows registered the raw mouse device");
            input.Toggle();
            input.OnKey(Key.W, true);
            input.OnKey(Key.Space, true);
            Application.DoEvents();
            SelfTest.Require(WaitFor(() => XInputGetState(slot, out var state) == 0
                && state.Pad.LY == 32767 && state.Pad.Buttons == 0x1000),
                "keyboard events reach XInput on the next UI dispatch", log);
            input.OnKey(Key.Space, false);
            Application.DoEvents();
            SelfTest.Require(WaitFor(() => XInputGetState(slot, out var state) == 0
                && state.Pad.LY == 32767 && state.Pad.Buttons == 0),
                "key release reaches XInput without polling", log);
            input.OnKey(Key.MouseLeft, true);
            input.OnKey(Key.MouseRight, true);
            input.OnKey(Key.MouseMiddle, true);
            Application.DoEvents();
            SelfTest.Require(WaitFor(() => XInputGetState(slot, out var state) == 0
                && state.Pad.LT == 255 && state.Pad.RT == 255 && state.Pad.Buttons == 0x0080),
                "mouse button bindings reach Windows XInput as LT, RT and R3", log);
            input.OnKey(Key.MouseLeft, false);
            input.OnKey(Key.MouseRight, false);
            input.OnKey(Key.MouseMiddle, false);
            Application.DoEvents();
            SelfTest.Require(WaitFor(() => XInputGetState(slot, out var state) == 0
                && state.Pad.LT == 0 && state.Pad.RT == 0 && state.Pad.Buttons == 0),
                "mouse button release reaches Windows XInput", log);
            input.OnMouseMove(4, -2, Stopwatch.GetTimestamp());
            Application.DoEvents();
            SelfTest.Require(WaitFor(() => XInputGetState(slot, out var state) == 0
                && state.Pad.LY == 32767 && state.Pad.RX > 0 && state.Pad.RY > 0),
                "mouse camera reaches the virtual right stick", log);
            input.ExpireMouse(Stopwatch.GetTimestamp() + Stopwatch.Frequency);
            Application.DoEvents();
            SelfTest.Require(WaitFor(() => XInputGetState(slot, out var state) == 0
                && state.Pad.LY == 32767 && state.Pad.RX == 0 && state.Pad.RY == 0),
                "mouse camera returns to neutral without releasing the movement key", log);
            input.OnKey(Key.F8, true);
            Application.DoEvents();
            SelfTest.Require(WaitFor(() => XInputGetState(slot, out var state) == 0
                && state.Pad.Equals(default(PadState))),
                "F8 sends neutral input on the next UI dispatch", log);
        }
        SelfTest.Require(WaitFor(() => XInputGetState(slot, out _) == 1167), "controller removed on exit", log);
    }
}
