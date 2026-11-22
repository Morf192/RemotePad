using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Exceptions;

namespace RemotePad;

internal sealed class VirtualPad : IDisposable
{
    private readonly ViGEmClient client = new();
    private readonly IXbox360Controller controller;

    public VirtualPad()
    {
        try
        {
            controller = client.CreateXbox360Controller();
            controller.AutoSubmitReport = false;
            controller.Connect();
            Send(default);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Send(PadState state)
    {
        controller.ButtonState = state.Buttons;
        controller.LeftTrigger = state.LT;
        controller.RightTrigger = state.RT;
        controller.LeftThumbX = state.LX;
        controller.LeftThumbY = state.LY;
        controller.RightThumbX = state.RX;
        controller.RightThumbY = state.RY;
        controller.SubmitReport();
    }

    public void Dispose()
    {
        try
        {
            try { Send(default); }
            finally { controller.Disconnect(); }
        }
        finally { client.Dispose(); }
    }
}

internal sealed class InputHooks : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(IntPtr name);

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    private readonly HookProc keyboardCallback, mouseCallback;
    private readonly KeyboardInput input;
    private readonly IntPtr window;
    private readonly HashSet<Key> mouseButtonsOverWindow = new();
    private IntPtr keyboardHook, mouseHook;

    public InputHooks(KeyboardInput input, IntPtr window)
    {
        this.input = input;
        this.window = window;
        keyboardCallback = HandleKeyboard;
        mouseCallback = HandleMouse;
        IntPtr module = GetModuleHandle(IntPtr.Zero);
        keyboardHook = SetWindowsHookEx(13, keyboardCallback, module, 0);
        if (keyboardHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        mouseHook = SetWindowsHookEx(14, mouseCallback, module, 0);
        if (mouseHook != IntPtr.Zero) return;
        var error = new Win32Exception(Marshal.GetLastWin32Error());
        Dispose();
        throw error;
    }

    private IntPtr HandleKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            int type = message.ToInt32();
            bool down = type is 0x100 or 0x104;
            if ((down || type is 0x101 or 0x105)
                && input.OnKey((Key)Marshal.ReadInt32(data), down))
                return new IntPtr(1);
        }
        return CallNextHookEx(keyboardHook, code, message, data);
    }

    private IntPtr HandleMouse(int code, IntPtr message, IntPtr data)
    {
        int type = message.ToInt32();
        if (code >= 0 && type is 0x201 or 0x202 or 0x204 or 0x205 or 0x207 or 0x208 or 0x20B or 0x20C
            && MouseButtonMessage.TryGet(type, unchecked((uint)Marshal.ReadInt32(data, 8)), out Key key, out bool down))
        {
            // Keep the emulator window operable even when its primary mouse button is mapped.
            bool passThrough = down
                ? GetAncestor(WindowFromPoint(Marshal.PtrToStructure<ScreenPoint>(data)), 3) == window
                : mouseButtonsOverWindow.Remove(key);
            if (passThrough)
            {
                if (down) mouseButtonsOverWindow.Add(key);
            }
            else if (input.OnKey(key, down))
            {
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(mouseHook, code, message, data);
    }

    public void Dispose()
    {
        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
        mouseHook = keyboardHook = IntPtr.Zero;
        GC.KeepAlive(mouseCallback);
        GC.KeepAlive(keyboardCallback);
    }
}

internal sealed class PadWindow : Form
{
    private readonly VirtualPad pad;
    private readonly KeyboardInput input;
    private readonly Label status = new() { Dock = DockStyle.Top, Height = 50, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label layoutHint = new() { Dock = DockStyle.Fill, Padding = new Padding(16, 6, 12, 0) };
    private readonly Button toggle = new() { Dock = DockStyle.Fill };
    private readonly Timer mouseIdle = new() { Interval = MouseAim.IdleMilliseconds };
    private bool flushQueued;
    private bool rawMouseRegistered;

    public Exception? SendError { get; private set; }

    public PadWindow(VirtualPad pad, KeyboardInput input)
    {
        this.pad = pad;
        this.input = input;
        Text = "RemotePad";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(450, 195);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 10);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 42 };
        var settings = new Button { Dock = DockStyle.Right, Width = 168, Text = "Controls…" };
        footer.Controls.Add(toggle);
        footer.Controls.Add(settings);
        Controls.Add(layoutHint);
        Controls.Add(status);
        Controls.Add(footer);
        toggle.Click += (_, _) => input.Toggle();
        settings.Click += (_, _) => EditControls();
        mouseIdle.Tick += (_, _) =>
        {
            input.ExpireMouse(Stopwatch.GetTimestamp());
            if (!input.MouseMoving) mouseIdle.Stop();
        };
        input.ReportAvailable += ScheduleFlush;
        RefreshLayout();
        RefreshStatus();
    }

    public void EnableRawMouse()
    {
        try
        {
            RawMouseInput.Register(Handle);
            rawMouseRegistered = true;
        }
        catch (Win32Exception error)
        {
            MessageBox.Show(this, "Mouse camera is unavailable. You can still use the keyboard to move the camera.\n" + error.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshLayout();
    }

    protected override void WndProc(ref Message message)
    {
        if (rawMouseRegistered && message.Msg == RawMouseInput.MessageId && input.Active && input.Settings.MouseCamera
            && RawMouseInput.TryRead(message.LParam, out int dx, out int dy))
        {
            input.OnMouseMove(dx, dy, Stopwatch.GetTimestamp());
            if (input.MouseMoving)
            {
                mouseIdle.Stop();
                mouseIdle.Start();
            }
        }
        base.WndProc(ref message);
    }

    private void EditControls()
    {
        input.BeginEditing();
        ControlSettings settings = input.Settings;
        try
        {
            using var dialog = new ControlsDialog(settings);
            if (dialog.ShowDialog(this) == DialogResult.OK) settings = dialog.Result;
        }
        finally
        {
            input.EndEditing(settings);
            RefreshLayout();
            RefreshStatus();
        }
    }

    private void RefreshLayout()
    {
        ControlLayout layout = input.Settings.Layout;
        string KeyFor(PadControl control) => ControlsDialog.KeyName(layout[control]);
        string movement = string.Join(" / ", KeyFor(PadControl.MoveUp), KeyFor(PadControl.MoveLeft),
            KeyFor(PadControl.MoveDown), KeyFor(PadControl.MoveRight));
        string camera = string.Join(" / ", KeyFor(PadControl.LookUp), KeyFor(PadControl.LookLeft),
            KeyFor(PadControl.LookDown), KeyFor(PadControl.LookRight));
        if (rawMouseRegistered && input.Settings.MouseCamera) camera = "mouse or " + camera;
        layoutHint.Text = $"Move — {movement}\nCamera — {camera}\nLT — {KeyFor(PadControl.LT)}    RT — {KeyFor(PadControl.RT)}    R3 — {KeyFor(PadControl.R3)}\nLB — {KeyFor(PadControl.LB)}    RB — {KeyFor(PadControl.RB)}    A / × — {KeyFor(PadControl.A)}";
    }

    private void ScheduleFlush()
    {
        if (flushQueued || IsDisposed) return;
        flushQueued = true;
        // Post to the UI loop so the low-level key hook returns before driver I/O.
        BeginInvoke((Action)Flush);
    }

    private void RefreshStatus()
    {
        string key = ControlsDialog.KeyName(input.Settings.Layout[PadControl.Pause]);
        string caption = input.Active ? $"Pause ({key})" : $"Enable ({key})";
        if (toggle.Text == caption) return;
        status.Text = input.Active ? "Emulation enabled" : "Controller connected · input paused";
        status.ForeColor = input.Active ? Color.DarkGreen : SystemColors.ControlText;
        toggle.Text = caption;
    }

    private void Flush()
    {
        flushQueued = false;
        try
        {
            while (input.TryReadReport(out PadState state)) pad.Send(state);
            if (!input.MouseMoving) mouseIdle.Stop();
            RefreshStatus();
        }
        catch (Exception error)
        {
            SendError = error;
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            input.ReportAvailable -= ScheduleFlush;
            mouseIdle.Dispose();
            if (rawMouseRegistered)
            {
                rawMouseRegistered = false;
                RawMouseInput.Unregister();
            }
        }
        base.Dispose(disposing);
    }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (args.Length != 0)
            {
                if (args.Length != 1 || args[0] is not ("--self-test" or "--driver-test"))
                    throw new ArgumentException("Use --self-test or --driver-test.");
                using var report = new System.IO.StringWriter();
                try
                {
                    SelfTest.Run(report.WriteLine);
                    if (args[0] == "--driver-test") DriverTest.Run(report.WriteLine);
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException(report.ToString() + "FAIL " + error.Message, error);
                }
                MessageBox.Show(report.ToString(), "RemotePad Test Results",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            Run();
            return 0;
        }
        catch (VigemBusNotFoundException)
        {
            MessageBox.Show("The ViGEmBus driver is missing. See the README for the official installer.",
                "RemotePad", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "RemotePad", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void Run()
    {
        ControlSettings settings;
        try
        {
            settings = ControlSettingsFile.Load(ControlSettingsFile.Pathname);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            settings = ControlSettings.Default();
            MessageBox.Show("Could not load saved controls. Using the standard layout.\n" + error.Message, "RemotePad",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        var input = new KeyboardInput(settings);
        Exception? sendError = null;
        try
        {
            using var pad = new VirtualPad();
            using var window = new PadWindow(pad, input);
            window.EnableRawMouse();
            using (var hooks = new InputHooks(input, window.Handle))
                Application.Run(window);
            sendError = window.SendError;
        }
        catch when (sendError != null)
        {
            // Preserve the original report failure if driver cleanup fails as well.
        }

        if (sendError != null)
            throw new InvalidOperationException("Could not send controller input. " + sendError.Message, sendError);
    }
}
