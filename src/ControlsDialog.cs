using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RemotePad;

internal sealed class ControlsDialog : Form
{
    private readonly DataGridView bindings = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, RowHeadersVisible = false,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle
    };
    private readonly CheckBox mouseCamera = new() { Text = "Камера мышью", AutoSize = true };
    private readonly CheckBox invert = new() { Text = "Инверсия по вертикали", AutoSize = true };
    private readonly NumericUpDown sensitivity = new() { Minimum = 10, Maximum = 300, Increment = 5, Width = 92 };
    private ControlLayout draft;
    public ControlSettings Result { get; private set; }

    public ControlsDialog(ControlSettings settings)
    {
        Result = settings;
        draft = settings.Layout;
        Text = "RemotePad — управление";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimumSize = new Size(570, 490);
        ClientSize = new Size(660, 600);
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 10);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(content);

        mouseCamera.Checked = settings.MouseCamera;
        sensitivity.Value = settings.Sensitivity;
        invert.Checked = settings.InvertY;
        mouseCamera.CheckedChanged += (_, _) => RefreshMouseOptions();
        RefreshMouseOptions();
        var mouse = Row();
        mouse.Controls.AddRange(new Control[] { mouseCamera,
            new Label { Text = "Чувствительность, %", AutoSize = true, Margin = new Padding(12, 6, 2, 0) },
            sensitivity, invert });
        content.Controls.Add(mouse);

        var presets = Row();
        var standard = new Button { Text = "Стандарт", AutoSize = true };
        var classic = new Button { Text = "Классика с IJKL", AutoSize = true };
        var rebind = new Button { Text = "Переназначить", AutoSize = true };
        standard.Click += (_, _) => LoadStandard();
        classic.Click += (_, _) => LoadPreset(ControlLayout.Classic());
        rebind.Click += (_, _) => Rebind();
        presets.Controls.AddRange(new Control[] { standard, classic, rebind });
        content.Controls.Add(presets);

        content.Controls.Add(new Label { AutoSize = true, Margin = new Padding(3, 7, 3, 10),
            Text = "Двойной щелчок меняет назначение. Занятые кнопки меняются местами." });
        bindings.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Действие", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        bindings.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Клавиша или мышь", Width = 160 });
        foreach (PadControl action in ControlLayout.Actions)
            bindings.Rows.Add(ActionName(action), KeyName(draft[action]));
        bindings.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Rebind(); };
        content.Controls.Add(bindings);

        var footer = Row();
        footer.FlowDirection = FlowDirection.RightToLeft;
        var save = new Button { Text = "Сохранить", AutoSize = true };
        var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => Save();
        footer.Controls.AddRange(new Control[] { save, cancel });
        content.Controls.Add(footer);
        CancelButton = cancel;
    }

    private static FlowLayoutPanel Row() => new()
    {
        Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4)
    };

    private void RefreshMouseOptions() => sensitivity.Enabled = invert.Enabled = mouseCamera.Checked;

    private void LoadStandard()
    {
        ControlSettings standard = ControlSettings.Default();
        LoadPreset(standard.Layout);
        mouseCamera.Checked = standard.MouseCamera;
        sensitivity.Value = standard.Sensitivity;
        invert.Checked = standard.InvertY;
    }

    private void LoadPreset(ControlLayout layout)
    {
        draft = layout;
        foreach (PadControl action in ControlLayout.Actions)
            bindings.Rows[(int)action].Cells[1].Value = KeyName(draft[action]);
    }

    private void Rebind()
    {
        if (bindings.CurrentRow == null) return;
        var action = (PadControl)bindings.CurrentRow.Index;
        using var capture = new KeyCaptureDialog(ActionName(action), allowMouse: action != PadControl.Pause);
        if (capture.ShowDialog(this) != DialogResult.OK) return;
        try { LoadPreset(draft.WithKey(action, capture.SelectedKey)); }
        catch (ArgumentException error) { MessageBox.Show(this, error.Message, "Назначение кнопки"); }
    }

    private void Save()
    {
        var settings = new ControlSettings(draft, mouseCamera.Checked, (int)sensitivity.Value, invert.Checked);
        try
        {
            ControlSettingsFile.Save(ControlSettingsFile.Pathname, settings);
            Result = settings;
            DialogResult = DialogResult.OK;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, error.Message, "Не удалось сохранить раскладку",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static string KeyName(Key key) => (int)key switch
    {
        (int)Key.MouseLeft => "ЛКМ",
        (int)Key.MouseRight => "ПКМ",
        (int)Key.MouseMiddle => "СКМ",
        (int)Key.MouseX1 => "Мышь 4",
        (int)Key.MouseX2 => "Мышь 5",
        0x20 => "Пробел",
        0x10 => "Shift",
        0x11 => "Ctrl",
        0x12 => "Alt",
        0x0D => "Enter",
        0x1B => "Esc",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        _ => new KeysConverter().ConvertToString((Keys)(int)key) ?? key.ToString()
    };

    private static string ActionName(PadControl action) => action switch
    {
        PadControl.MoveUp => "Движение вперёд",
        PadControl.MoveDown => "Движение назад",
        PadControl.MoveLeft => "Движение влево",
        PadControl.MoveRight => "Движение вправо",
        PadControl.LookUp => "Камера вверх",
        PadControl.LookDown => "Камера вниз",
        PadControl.LookLeft => "Камера влево",
        PadControl.LookRight => "Камера вправо",
        PadControl.DpadUp => "Крестовина ↑",
        PadControl.DpadDown => "Крестовина ↓",
        PadControl.DpadLeft => "Крестовина ←",
        PadControl.DpadRight => "Крестовина →",
        PadControl.View => "View / Share",
        PadControl.Menu => "Menu / Options",
        PadControl.Guide => "Xbox / PS",
        PadControl.Pause => "Включить или приостановить RemotePad",
        _ => action.ToString()
    };
}

internal sealed class KeyCaptureDialog : Form, IMessageFilter
{
    private readonly Label prompt = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button cancel = new() { Dock = DockStyle.Bottom, Height = 32, Text = "Отмена", DialogResult = DialogResult.Cancel };
    private readonly bool allowMouse;
    private Key? pressed;
    public Key SelectedKey { get; private set; }

    public KeyCaptureDialog(string action, bool allowMouse)
    {
        this.allowMouse = allowMouse;
        Text = "Назначение клавиши";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(390, 116);
        Font = new Font("Segoe UI", 10);
        prompt.Text = (allowMouse ? "Нажми и отпусти клавишу или кнопку мыши" : "Нажми и отпусти клавишу") + "\n" + action;
        Controls.Add(prompt);
        Controls.Add(cancel);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Application.AddMessageFilter(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Application.RemoveMessageFilter(this);
        base.OnFormClosed(e);
    }

    public bool PreFilterMessage(ref Message message)
    {
        if (ActiveForm != this) return false;
        Key key;
        bool down;
        if (message.Msg is 0x100 or 0x101 or 0x104 or 0x105)
        {
            key = ControlLayout.Normalize((Key)message.WParam.ToInt32());
            down = message.Msg is 0x100 or 0x104;
        }
        else if (!allowMouse || (pressed == null && message.HWnd == cancel.Handle)
            || !MouseButtonMessage.TryGet(message.Msg, unchecked((uint)message.WParam.ToInt64()), out key, out down))
        {
            return false;
        }

        if (down)
        {
            if (pressed == null && ControlLayout.IsBindable(key))
            {
                pressed = key;
                if (ControlLayout.IsMouse(key)) Capture = true;
            }
            else if (!ControlLayout.IsBindable(key)) prompt.Text = "Эту клавишу нельзя назначить. Нажми другую.";
        }
        else if (pressed == key)
        {
            if (ControlLayout.IsMouse(key)) Capture = false;
            SelectedKey = key;
            DialogResult = DialogResult.OK;
        }
        return true;
    }
}
