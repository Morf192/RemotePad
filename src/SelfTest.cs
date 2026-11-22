using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RemotePad;

internal static class SelfTest
{
    internal static void Require(bool condition, string check, Action<string> log)
    {
        if (!condition) throw new InvalidOperationException(check);
        log("PASS " + check);
    }

    private static void TapF8(KeyboardInput input)
    {
        input.OnKey(Key.F8, true);
        input.OnKey(Key.F8, false);
    }

    public static void Run(Action<string> log)
    {
        void Check(bool condition, string name) => Require(condition, name, log);

        var input = new KeyboardInput(new ControlSettings(ControlLayout.Classic(), false, 100, false));
        PadState neutral = default;
        Check(!input.Active && input.Read().Equals(neutral) && !input.TryReadReport(out _),
            "starts paused and neutral");
        Check(Marshal.SizeOf<PadState>() == 12 && Marshal.OffsetOf<PadState>(nameof(PadState.LX)).ToInt32() == 4,
            "XInput gamepad layout");
        Check(!input.OnKey(Key.W, true) && !input.OnKey(Key.W, false), "paused keys pass through");
        bool f8 = input.OnKey(Key.F8, true) && input.OnKey(Key.F8, true) && input.OnKey(Key.F8, false);
        Check(f8 && input.Active && input.TryReadReport(out var state) && state.Equals(neutral)
            && !input.TryReadReport(out _), "F8 is consumed and auto-repeat toggles only once");

        input.OnKey(Key.W, true);
        input.OnKey(Key.D, true);
        Check(input.Read().LX == 23170 && input.Read().LY == 23170, "left stick diagonal normalized");
        input.OnKey(Key.S, true);
        Check(input.Read().LX == 32767 && input.Read().LY == 0, "opposite directions cancel");
        input.OnKey(Key.J, true);
        input.OnKey(Key.K, true);
        Check(input.Read().RX == -23170 && input.Read().RY == -23170 && input.Read().LX == 32767,
            "right stick independent");
        input.OnKey(Key.LeftShift, true);
        input.OnKey(Key.RightShift, true);
        input.OnKey(Key.LeftShift, false);
        Check((input.Read().Buttons & 0x0040) != 0, "both Shift keys track independently");
        input.OnKey(Key.LeftControl, true);
        input.OnKey(Key.RightControl, true);
        input.OnKey(Key.RightControl, false);
        Check((input.Read().Buttons & 0x2000) != 0, "both Ctrl keys track independently");
        input.OnKey(Key.LeftControl, false);

        input.OnKey(Key.Space, true);
        input.OnKey(Key.E, true);
        input.OnKey(Key.Z, true);
        input.OnKey(Key.X, true);
        Check(input.Read().Buttons == 0x9040 && input.Read().LT == 255 && input.Read().RT == 255,
            "multiple buttons and triggers");
        while (input.TryReadReport(out _)) { }
        Check(input.OnKey(Key.Space, true) && !input.TryReadReport(out _), "key repeat does not duplicate reports");
        input.OnKey(Key.Space, false);
        Check((input.Read().Buttons & 0x1000) == 0, "button release");

        TapF8(input);
        Check(!input.Active && input.Read().Equals(neutral) && input.TryReadReport(out state)
            && state.Equals(neutral) && !input.TryReadReport(out _), "pause replaces all pending input with neutral");
        Check(input.OnKey(Key.W, true) && input.OnKey(Key.W, false) && !input.TryReadReport(out _),
            "captured key repeat and release stay consumed across pause");
        Check(!input.OnKey(Key.I, true), "a new key passes through during pause");
        TapF8(input);
        Check(input.Active && input.Read().Equals(neutral) && input.OnKey(Key.D, true)
            && !input.OnKey(Key.I, true) && !input.OnKey(Key.I, false) && input.Read().Equals(neutral),
            "resume ignores keys held across toggle");
        Check(input.OnKey(Key.I, true) && input.Read().RY == 32767, "fresh press works after resume");

        input.Toggle();
        input.Toggle();
        bool tapped = input.OnKey(Key.Space, true) && input.OnKey(Key.Space, false);
        Check(tapped && input.TryReadReport(out state) && state.Equals(neutral)
            && input.TryReadReport(out state) && state.Buttons == 0x1000
            && input.TryReadReport(out state) && state.Equals(neutral) && !input.TryReadReport(out _),
            "rapid pause/resume retains neutral before new input and preserves a quick tap");
        Check(!input.OnKey((Key)0x70, true) && !input.OnKey((Key)0x70, false), "unmapped keys pass through");

        var settings = ControlSettings.Default();
        Check(settings.Layout[PadControl.LookUp] == Key.Up && settings.Layout[PadControl.DpadUp] == Key.I,
            "arrow preset keeps all actions bound");
        Check(settings.Layout[PadControl.LB] == Key.Q && settings.Layout[PadControl.RB] == Key.E
            && settings.Layout[PadControl.B] == Key.X && settings.Layout[PadControl.Y] == Key.R
            && settings.Layout[PadControl.LT] == Key.MouseLeft && settings.Layout[PadControl.RT] == Key.MouseRight
            && settings.Layout[PadControl.R3] == Key.MouseMiddle && settings.MouseCamera
            && settings.Sensitivity == 100 && !settings.InvertY, "standard preset matches the requested actions and mouse settings");
        Check(MouseButtonMessage.TryGet(0x201, 0, out Key click, out bool down) && click == Key.MouseLeft && down
            && MouseButtonMessage.TryGet(0x205, 0, out click, out down) && click == Key.MouseRight && !down
            && MouseButtonMessage.TryGet(0x20B, 2u << 16, out click, out down) && click == Key.MouseX2 && down,
            "mouse button messages decode press, release and side buttons");
        var mouseInput = new KeyboardInput(settings);
        Check(!mouseInput.OnKey(Key.MouseLeft, true) && !mouseInput.OnKey(Key.MouseLeft, false),
            "mouse clicks pass through while paused");
        mouseInput.Toggle();
        Check(mouseInput.OnKey(Key.MouseLeft, true) && mouseInput.OnKey(Key.MouseRight, true)
            && mouseInput.OnKey(Key.MouseMiddle, true) && mouseInput.Read().LT == 255
            && mouseInput.Read().RT == 255 && mouseInput.Read().Buttons == 0x0080,
            "left, right and middle clicks press LT, RT and R3 together");
        Check(mouseInput.OnKey(Key.MouseLeft, false) && mouseInput.Read().LT == 0 && mouseInput.Read().RT == 255
            && mouseInput.OnKey(Key.MouseRight, false) && mouseInput.Read().RT == 0 && mouseInput.Read().Buttons == 0x0080
            && mouseInput.OnKey(Key.MouseMiddle, false) && mouseInput.Read().Buttons == 0,
            "releasing mouse buttons releases only their mapped gamepad buttons");
        bool rejectsMousePause = false;
        try { settings.Layout.WithKey(PadControl.Pause, Key.MouseMiddle); }
        catch (ArgumentException) { rejectsMousePause = true; }
        Check(rejectsMousePause, "pause can always be operated by keyboard");
        var changed = settings with { Layout = settings.Layout.WithKey(PadControl.A, Key.E)
            .WithKey(PadControl.Pause, (Key)0x78), Sensitivity = 175, InvertY = true };
        Check(changed.Layout[PadControl.RB] == Key.Space && settings.Layout[PadControl.RB] == Key.E,
            "rebinding swaps occupied assignments without changing the original");
        input.BeginEditing();
        Check(!input.Active && !input.OnKey(Key.F8, true), "editing pauses the controller and releases keyboard capture");
        input.EndEditing(changed);
        Check(!input.OnKey(Key.F8, true) && input.OnKey((Key)0x78, true) && input.Active,
            "a customized pause key replaces F8");
        input.OnKey((Key)0x78, false);
        input.OnKey(Key.E, true);
        Check(input.Read().Buttons == 0x1000, "updated layout drives the selected gamepad button");

        const long start = 100;
        input.OnMouseMove(3, 2, start);
        PadState aimed = input.Read();
        Check(aimed.RX > 0 && aimed.RY > 0, "mouse drives the right stick with the selected vertical inversion");
        input.ExpireMouse(start + Stopwatch.Frequency);
        Check(!input.MouseMoving && input.Read().RX == 0 && input.Read().RY == 0 && input.Read().Buttons == 0x1000,
            "mouse idle releases the right stick and preserves keyboard input");
        var aim = new MouseAim();
        aim.Move(2, -1, start, settings);
        short regular = aim.X;
        Check(aim.Y > 0, "moving the mouse up moves the right stick up by default");
        aim.Clear();
        aim.Move(2, -1, start, changed);
        Check(aim.X > regular, "mouse sensitivity increases stick deflection");
        aim.Move(-4000, 0, start + 1, changed);
        Check(aim.X < -32000, "fast mouse movement saturates safely and reverses immediately");

        string directory = Path.Combine(Path.GetTempPath(), "RemotePad-test-" + Guid.NewGuid().ToString("N"));
        string config = Path.Combine(directory, "controls.json");
        try
        {
            ControlSettingsFile.Save(config, settings);
            ControlSettingsFile.Save(config, changed);
            ControlSettings saved = ControlSettingsFile.Load(config);
            Check(saved.Layout[PadControl.A] == Key.E && saved.Layout[PadControl.Pause] == (Key)0x78
                && saved.MouseCamera && saved.InvertY && saved.Sensitivity == 175,
                "custom controls and mouse settings survive a save and reload");
        }
        finally
        {
            File.Delete(config);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
}
