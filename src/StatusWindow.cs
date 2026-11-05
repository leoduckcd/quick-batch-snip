using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;

internal sealed class StatusWindow : Form
{
    private static readonly Color Surface = Color.FromArgb(239, 244, 245);
    private static readonly Color Ink = Color.FromArgb(21, 43, 51);
    private static readonly Color Muted = Color.FromArgb(69, 91, 98);
    private static readonly Color Border = Color.FromArgb(207, 222, 224);
    private static readonly Color Accent = Color.FromArgb(150, 242, 202);
    private static readonly Color AccentInk = Color.FromArgb(12, 73, 56);
    private readonly Font body = new Font("Segoe UI", 10);
    private readonly Font strong = new Font("Segoe UI Semibold", 10);
    private readonly Font small = new Font("Segoe UI Semibold", 8.5f);
    private readonly Font heading = new Font("Segoe UI", 25, FontStyle.Bold);
    private readonly WindowsStartup startup = new WindowsStartup(Application.ExecutablePath);
    private readonly Label count;
    private readonly StartupToggle startupToggle;

    internal StatusWindow(Icon icon, Action exit)
    {
        SuspendLayout();
        Text = "Quick Batch Snip 1.5.1";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(548, 473);
        BackColor = Surface;
        Font = body;

        var hero = new Hero { Bounds = new Rectangle(22, 20, 504, 176) };
        hero.Controls.Add(MakeLabel("PORTABLE SCREENSHOT TOOL", small, Accent, 165, 40));
        hero.Controls.Add(MakeLabel("Quick Batch Snip", heading, Color.White, 162, 60));
        hero.Controls.Add(MakeLabel("Queue screenshots and paste them together.", body,
            Color.FromArgb(210, 229, 230), 165, 113));
        Controls.Add(hero);

        Controls.Add(ShortcutCard(22, "Open capture", "Win", "S", "Drag to select a region,\r\nor press Enter for all displays."));
        Controls.Add(ShortcutCard(280, "Paste the batch", "Ctrl", "V", "Paste into the destination app."));

        var startupCard = new Card(Color.White, Border, 15)
        {
            Bounds = new Rectangle(22, 344, 504, 60),
            TabIndex = 0
        };
        startupToggle = new StartupToggle(small)
        {
            Name = "startupToggle",
            Text = "Start with Windows",
            Font = strong,
            Bounds = new Rectangle(16, 5, 472, 50)
        };
        startupToggle.Click += delegate { UpdateStartupSetting(true); };
        startupCard.Controls.Add(startupToggle);
        Controls.Add(startupCard);

        var status = new Card(Color.FromArgb(222, 239, 233), Color.Empty, 15)
        {
            Bounds = new Rectangle(22, 420, 209, 32)
        };
        var dot = MakeLabel("●", small, Color.FromArgb(21, 113, 81), 8, 0);
        dot.AutoSize = false;
        dot.Size = new Size(16, 32);
        dot.TextAlign = ContentAlignment.MiddleCenter;
        status.Controls.Add(dot);
        count = MakeLabel("0 screenshots queued", small, AccentInk, 26, 0);
        count.AutoSize = false;
        count.Size = new Size(177, 32);
        count.TextAlign = ContentAlignment.MiddleLeft;
        status.Controls.Add(count);
        Controls.Add(status);

        var hide = new DashboardButton("Hide to tray")
        {
            Bounds = new Rectangle(339, 417, 113, 38),
            TabIndex = 1
        };
        hide.Click += delegate { Hide(); };
        Controls.Add(hide);

        var quit = new DashboardButton("Exit")
        {
            Name = "exitButton",
            Bounds = new Rectangle(460, 417, 66, 38),
            TabIndex = 2
        };
        quit.Click += delegate { exit(); };
        Controls.Add(quit);

        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true);
    }

    internal void SetCount(string text)
    {
        count.Text = text;
    }

    internal void RefreshStartupSetting()
    {
        UpdateStartupSetting(false);
    }

    private void UpdateStartupSetting(bool toggle)
    {
        string hint;
        try
        {
            bool legacyCleared = !toggle || startup.SetEnabled(!startup.Enabled);
            startupToggle.Checked = startup.Enabled;
            if (!legacyCleared)
                hint = "Check for the old entry in Windows Startup settings.";
            else if (startupToggle.Checked && !startup.UsesThisCopy())
                hint = "Turn off, then on to refresh this startup shortcut.";
            else
                hint = "Open this window when you sign in.";
        }
        catch (Exception ex)
        {
            if (!(ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException || ex is COMException))
                throw;
            hint = "Windows could not access the startup shortcut.";
        }
        startupToggle.AccessibleDescription = hint;
        startupToggle.Invalidate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            heading.Dispose();
            small.Dispose();
            strong.Dispose();
            body.Dispose();
        }
    }

    private Card ShortcutCard(int left, string title, string modifier, string key, string hint)
    {
        var card = new Card(Color.White, Border, 15) { Bounds = new Rectangle(left, 208, 246, 124) };
        card.Controls.Add(MakeLabel(title, strong, Ink, 16, 12));
        card.Controls.Add(Keycap(modifier, 16, 47));
        card.Controls.Add(MakeLabel("+", body, Muted, 68, 48));
        card.Controls.Add(Keycap(key, 88, 31));
        card.Controls.Add(MakeLabel(hint, small, Muted, 16, 80));
        return card;
    }

    private Card Keycap(string text, int left, int width)
    {
        var key = new Card(Surface, Border, 6) { Bounds = new Rectangle(left, 43, width, 29) };
        var label = MakeLabel(text, strong, Ink, 0, 0);
        label.AutoSize = false;
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleCenter;
        key.Controls.Add(label);
        return key;
    }

    private static Label MakeLabel(string text, Font font, Color color, int x, int y)
    {
        return new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = color,
            Font = font,
            Location = new Point(x, y),
            Text = text,
            UseMnemonic = false
        };
    }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class Hero : Panel
    {
        internal Hero()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            Graphics g = e.Graphics;
            GraphicsState saved = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(Width / 504f, Height / 176f);
            using (GraphicsPath card = Rounded(new RectangleF(0, 0, 504, 176), 21))
            using (var fill = new LinearGradientBrush(new Rectangle(0, 0, 504, 176),
                Color.FromArgb(18, 62, 66), Color.FromArgb(11, 28, 43), 16))
            {
                g.FillPath(fill, card);
                g.SetClip(card, CombineMode.Intersect);
                using (var lines = new Pen(Color.FromArgb(24, Accent), 1))
                {
                    for (int ring = 0; ring < 4; ring++)
                    {
                        int offset = ring * 19;
                        g.DrawEllipse(lines, 376 - offset, -118 - offset, 225 + offset * 2, 225 + offset * 2);
                    }
                }
                DrawLogo(g);
            }
            g.Restore(saved);
        }

        private static void DrawLogo(Graphics g)
        {
            var bounds = new RectangleF(25, 27, 122, 122);
            using (GraphicsPath shadow = Rounded(new RectangleF(25, 32, 122, 122), 27))
            using (var shade = new SolidBrush(Color.FromArgb(38, 0, 10, 17)))
                g.FillPath(shade, shadow);

            using (GraphicsPath tile = Rounded(bounds, 27))
            using (var fill = new LinearGradientBrush(bounds,
                Color.FromArgb(39, 106, 104), Color.FromArgb(23, 54, 69), 66))
            using (var edge = new Pen(Color.FromArgb(105, Accent), 1))
            {
                g.FillPath(fill, tile);
                g.DrawPath(edge, tile);
            }

            using (var mark = new Pen(Accent, 5) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLines(mark, new[] { new Point(53, 74), new Point(53, 55), new Point(72, 55) });
                g.DrawLines(mark, new[] { new Point(100, 55), new Point(119, 55), new Point(119, 74) });
                g.DrawLines(mark, new[] { new Point(119, 102), new Point(119, 121), new Point(100, 121) });
                g.DrawLines(mark, new[] { new Point(72, 121), new Point(53, 121), new Point(53, 102) });
            }

            using (var edge = new Pen(Color.FromArgb(150, Accent), 2))
            using (GraphicsPath back = Rounded(new RectangleF(81, 76, 25, 22), 4))
                g.DrawPath(edge, back);
            using (var light = new SolidBrush(Color.FromArgb(239, 255, 249)))
            using (GraphicsPath front = Rounded(new RectangleF(68, 84, 29, 22), 4))
                g.FillPath(light, front);
        }
    }

    private sealed class Card : Panel
    {
        private readonly Color fill;
        private readonly Color border;
        private readonly int radius;

        internal Card(Color fill, Color border, int radius)
        {
            this.fill = fill;
            this.border = border;
            this.radius = radius;
            BackColor = Color.Transparent;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shape = Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1),
                radius * e.Graphics.DpiX / 96))
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, shape);
                if (!border.IsEmpty)
                    using (var pen = new Pen(border))
                        e.Graphics.DrawPath(pen, shape);
            }
        }
    }

    private sealed class DashboardButton : Button
    {
        private bool hovered;

        internal DashboardButton(string text)
        {
            Text = text;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            DoubleBuffered = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Surface);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = hovered ? Color.FromArgb(224, 235, 236) : Color.White;
            using (GraphicsPath shape = Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1),
                11 * e.Graphics.DpiX / 96))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Border))
            {
                e.Graphics.FillPath(brush, shape);
                e.Graphics.DrawPath(pen, shape);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -6, -6), Ink, fill);
        }
    }

    private sealed class StartupToggle : CheckBox
    {
        private readonly Font hintFont;

        internal StartupToggle(Font hintFont)
        {
            this.hintFont = hintFont;
            AutoCheck = false;
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            float scale = Height / 50f;
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width, (int)(28 * scale)),
                Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            var hint = new Rectangle(0, (int)(26 * scale), Width - (int)(56 * scale), (int)(21 * scale));
            TextRenderer.DrawText(e.Graphics, AccessibleDescription, hintFont, hint, Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var track = new RectangleF(Width - 44 * scale, 13 * scale, 44 * scale, 24 * scale);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shape = Rounded(track, 12 * scale))
            using (var brush = new SolidBrush(Checked ? Color.FromArgb(22, 121, 91) : Color.FromArgb(112, 139, 143)))
                e.Graphics.FillPath(brush, shape);
            using (var thumb = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(thumb, track.X + (Checked ? 23 : 3) * scale,
                    track.Y + 3 * scale, 18 * scale, 18 * scale);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Ceiling(RectangleF.Inflate(track, 3, 3)));
        }
    }
}
