using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using MechaCommunityMod.Configuration;

namespace MechaCommunityMod.Launcher;

// The same exported sprites and slice borders used by the native game UI.
internal sealed class LauncherSkin : IDisposable
{
    internal static readonly Color Background = Color.FromArgb(10, 14, 24);
    internal static readonly Color Surface = Color.FromArgb(18, 24, 40);
    internal static readonly Color Text = Color.FromArgb(190, 202, 235);
    internal static readonly Color Muted = Color.FromArgb(140, 155, 184);
    internal static readonly Color Gold = Color.FromArgb(255, 184, 75);
    private readonly Dictionary<string, Image> _images = new();
    private readonly PrivateFontCollection _fonts = new();
    private readonly List<(IntPtr Memory, IntPtr Registration)> _fontMemory = new();
    private bool _disposed;
    internal Font Body { get; }
    internal Font Small { get; }
    internal Font Title { get; }
    internal Font Button { get; }

    [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr data, uint length, IntPtr reserved, ref uint count);
    [DllImport("gdi32.dll")] private static extern bool RemoveFontMemResourceEx(IntPtr handle);

    internal LauncherSkin()
    {
        AddFont("RobotoCondensed-Regular.ttf"); AddFont("Xolonium-Bold.ttf");
        Body = new Font(_fonts.Families.Single(f => f.Name.Contains("Roboto")), 11);
        Small = new Font(Body.FontFamily, 10);
        Title = new Font(_fonts.Families.Single(f => f.Name.Contains("Xolonium")), 19, FontStyle.Bold);
        Button = new Font(Title.FontFamily, 8.5f, FontStyle.Bold);
    }

    // The installer uses a light background. Keep the same rating meanings
    // with readable colors on the launcher's dark panels.
    internal static Color Fairness(FairnessLevel level) => level switch {
        FairnessLevel.Green => Color.FromArgb(126, 213, 155),
        FairnessLevel.Yellow => Color.FromArgb(244, 194, 102),
        _ => Color.FromArgb(255, 141, 128)
    };

    private static Stream Resource(string name) => typeof(LauncherSkin).Assembly.GetManifestResourceStream(
        "MechaCommunityMod.Launcher.Assets." + name) ?? throw new InvalidDataException("Missing launcher artwork: " + name);

    private void AddFont(string name)
    {
        using var resource = Resource(name);
        using var bytes = new MemoryStream(); resource.CopyTo(bytes);
        var data = bytes.ToArray(); var memory = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, memory, data.Length);
        uint count = 0;
        var registration = AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, ref count);
        _fontMemory.Add((memory, registration));
        _fonts.AddMemoryFont(memory, data.Length);
    }

    internal Image Image(string name)
    {
        if (_images.TryGetValue(name, out var image)) return image;
        using var resource = Resource(name + ".png");
        using var original = System.Drawing.Image.FromStream(resource);
        return _images[name] = new Bitmap(original);
    }

    internal void Draw(Graphics graphics, string name, Rectangle target, bool disabled = false)
    {
        var image = Image(name);
        // Unity borders are left/bottom/right/top. Preserve the authored end caps
        // while stretching the middle; all dimensions follow the current DPI.
        var border = name == "replayUI_frame_2" ? new Padding(44, 77, 75, 46)
            : name.StartsWith("panel_") ? new Padding(10) : new Padding(54, 0, 54, 0);
        var factor = graphics.DpiX / 96f;
        var left = Math.Min(target.Width / 2, (int)(border.Left * factor));
        var right = Math.Min(target.Width - left, (int)(border.Right * factor));
        var top = Math.Min(target.Height / 2, (int)(border.Top * factor));
        var bottom = Math.Min(target.Height - top, (int)(border.Bottom * factor));
        int[] sx = { 0, border.Left, image.Width - border.Right, image.Width };
        int[] sy = { 0, border.Top, image.Height - border.Bottom, image.Height };
        int[] dx = { target.Left, target.Left + left, target.Right - right, target.Right };
        int[] dy = { target.Top, target.Top + top, target.Bottom - bottom, target.Bottom };
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        if (disabled) attributes.SetColorMatrix(new ColorMatrix { Matrix00 = .7f, Matrix11 = .7f, Matrix22 = .7f, Matrix33 = .7f, Matrix44 = 1 });
        for (var y = 0; y < 3; y++) for (var x = 0; x < 3; x++)
        {
            var source = new Rectangle(sx[x], sy[y], sx[x + 1] - sx[x], sy[y + 1] - sy[y]);
            var destination = new Rectangle(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]);
            if (source.Width > 0 && source.Height > 0 && destination.Width > 0 && destination.Height > 0)
                graphics.DrawImage(image, destination, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var image in _images.Values) image.Dispose();
        Body.Dispose(); Small.Dispose(); Title.Dispose(); Button.Dispose(); _fonts.Dispose();
        foreach (var (memory, registration) in _fontMemory)
        { if (registration != IntPtr.Zero) RemoveFontMemResourceEx(registration); Marshal.FreeHGlobal(memory); }
    }
}

internal sealed class FairnessIndicator : Control
{
    internal FairnessIndicator()
    {
        Size = new Size(24, 24); AccessibleRole = AccessibleRole.StaticText; TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var side = LogicalToDeviceUnits(9);
        using var brush = new SolidBrush(ForeColor);
        e.Graphics.FillEllipse(brush, (Width - side) / 2f, (Height - side) / 2f, side, side);
    }
}

internal sealed class GameButton : Button
{
    private readonly LauncherSkin _skin;
    private bool _hover, _pressed;
    internal bool Primary { get; set; }
    internal bool SettingsIcon { get; set; }
    internal GameButton(LauncherSkin skin)
    {
        _skin = skin; Font = skin.Button; ForeColor = LauncherSkin.Text;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = LauncherSkin.Background;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AutoSize = true; Margin = new Padding(0, 0, 8, 0); Padding = new Padding(18, 9, 18, 9);
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (SettingsIcon) return LogicalToDeviceUnits(new Size(38, 38));
        var size = TextRenderer.MeasureText(Text, Font);
        return new Size(Math.Max(LogicalToDeviceUnits(112), size.Width + LogicalToDeviceUnits(36)), LogicalToDeviceUnits(42));
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        _skin.Draw(e.Graphics, !Enabled ? "button_new_disable" : "button_" + (Primary ? "y" : "b") + "_m" + (_hover || _pressed ? "2" : "1"), ClientRectangle);
        using var brush = new SolidBrush(!Enabled ? LauncherSkin.Muted : Primary ? Color.FromArgb(255, 223, 163) : LauncherSkin.Text);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var text = ClientRectangle; if (_pressed) text.Offset(0, LogicalToDeviceUnits(1));
        if (SettingsIcon)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var cx = text.Left + text.Width / 2f; var cy = text.Top + text.Height / 2f;
            var radius = LogicalToDeviceUnits(9);
            var points = Enumerable.Range(0, 32).Select(i => {
                var angle = i * Math.PI / 16; var r = radius * (i % 4 is 0 or 3 ? 1f : .78f);
                return new PointF(cx + (float)Math.Cos(angle) * r, cy + (float)Math.Sin(angle) * r);
            }).ToArray();
            using var gear = new GraphicsPath(FillMode.Alternate);
            gear.AddPolygon(points); var hole = LogicalToDeviceUnits(3);
            gear.AddEllipse(cx - hole, cy - hole, hole * 2, hole * 2);
            e.Graphics.FillPath(brush, gear);
        }
        else e.Graphics.DrawString(Text, Font, brush, text, format);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -7, -7), ForeColor, BackColor);
    }
}

internal sealed class GameCheckBox : CheckBox
{
    private readonly LauncherSkin _skin;
    private bool _hover;
    internal GameCheckBox(LauncherSkin skin)
    {
        _skin = skin; AutoSize = true; Font = skin.Body; ForeColor = LauncherSkin.Text;
        FlatStyle = FlatStyle.Flat; Padding = new Padding(0, 6, 8, 6);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = TextRenderer.MeasureText(Text, Font);
        return new Size(size.Width + LogicalToDeviceUnits(38), Math.Max(size.Height + LogicalToDeviceUnits(14), LogicalToDeviceUnits(36)));
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var side = LogicalToDeviceUnits(19);
        var box = new Rectangle(LogicalToDeviceUnits(2), (Height - side) / 2, side, side);
        using var fill = new SolidBrush(Checked ? Color.FromArgb(40, 54, 78) : LauncherSkin.Background);
        using var pen = new Pen(_hover || Focused ? LauncherSkin.Gold : Color.FromArgb(91, 111, 153), Math.Max(1, DeviceDpi / 96f));
        e.Graphics.FillRectangle(fill, box); e.Graphics.DrawRectangle(pen, box);
        if (Checked) e.Graphics.DrawImage(_skin.Image("Checked-Icon"), Rectangle.Inflate(box, -3, -4));
        using var text = new SolidBrush(Enabled ? ForeColor : LauncherSkin.Muted);
        using var format = new StringFormat { LineAlignment = StringAlignment.Center };
        if (Text.Length > 0) e.Graphics.DrawString(Text, Font, text, new RectangleF(LogicalToDeviceUnits(31), 0, Width - LogicalToDeviceUnits(31), Height), format);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -2), ForeColor, BackColor);
    }
}

internal sealed class GameChoice : ComboBox
{
    internal GameChoice(LauncherSkin skin)
    {
        Font = skin.Body; DropDownStyle = ComboBoxStyle.DropDownList;
        Width = 220; BackColor = LauncherSkin.Background; ForeColor = LauncherSkin.Text;
        FlatStyle = FlatStyle.Flat; DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 24;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using var background = new SolidBrush(e.State.HasFlag(DrawItemState.Selected) ? Color.FromArgb(43, 61, 99) : LauncherSkin.Background);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index < 0) return;
        using var text = new SolidBrush(Enabled ? LauncherSkin.Text : LauncherSkin.Muted);
        e.Graphics.DrawString(Items[e.Index]?.ToString(), Font, text, e.Bounds);
        e.DrawFocusRectangle();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var outline = new Pen(Focused ? LauncherSkin.Gold : Color.FromArgb(88, 106, 150));
        e.Graphics.DrawRectangle(outline, 0, 0, Width - 1, Height - 1);
        using var text = new SolidBrush(Enabled ? LauncherSkin.Text : LauncherSkin.Muted);
        using var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        var inset = LogicalToDeviceUnits(7);
        e.Graphics.DrawString(Text, Font, text, new Rectangle(inset, 0, Width - inset * 5, Height), format);
        var x = Width - inset * 2; var y = Height / 2;
        e.Graphics.DrawLines(outline, new[] { new Point(x - inset, y - inset / 2), new Point(x, y + inset / 2), new Point(x + inset, y - inset / 2) });
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}

internal sealed class GameBanner : Control
{
    private readonly LauncherSkin _skin;
    internal GameBanner(LauncherSkin skin) { _skin = skin; DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(LauncherSkin.Background);
        var art = _skin.Image("LoadingBg03_20-9");
        var scale = DeviceDpi / 96f;
        var artwork = new Rectangle(Width - (int)(430 * scale), -(int)(25 * scale), (int)(430 * scale), (int)(194 * scale));
        g.DrawImage(art, artwork);
        var fade = new Rectangle(artwork.Left - 1, 0, (int)(205 * scale), Height);
        using var shade = new LinearGradientBrush(fade, LauncherSkin.Background, Color.FromArgb(0, LauncherSkin.Background), 0f);
        g.FillRectangle(shade, fade);
        using var gold = new SolidBrush(LauncherSkin.Gold);
        using var text = new SolidBrush(LauncherSkin.Text);
        g.DrawString("MECHABELLUM", _skin.Button, gold, 2 * scale, 12 * scale);
        g.DrawString("COMMUNITY MOD", _skin.Title, text, 0, 34 * scale);
        using var versionBackground = new SolidBrush(Color.FromArgb(195, LauncherSkin.Background));
        g.FillRectangle(versionBackground, Width - 74 * scale, 8 * scale, 70 * scale, 24 * scale);
        g.DrawString("v" + ModIdentity.Version, _skin.Small, text, Width - 70 * scale, 12 * scale);
        using var line = new Pen(Color.FromArgb(71, 90, 128));
        g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }
}
