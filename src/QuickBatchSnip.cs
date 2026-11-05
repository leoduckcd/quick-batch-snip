using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Quick Batch Snip")]
[assembly: AssemblyProduct("Quick Batch Snip")]
[assembly: AssemblyDescription("Portable Windows screenshot batch capture")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool startup = args.Length == 1 && args[0] == WindowsStartup.Argument;
        using (var instance = new Mutex(false, @"Local\QuickBatchSnip.69426C91"))
        {
            bool acquired;
            try { acquired = instance.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
            {
                if (!startup && !SnipApp.ShowRunningInstance())
                {
                    MessageBox.Show(
                        "Quick Batch Snip is already running. Look for its icon in the system tray, including the hidden icons. To switch from an older version, choose Exit in its tray menu, then open this file again.",
                        "Quick Batch Snip", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var app = new SnipApp(!startup))
                    Application.Run(app);
            }
            catch (Win32Exception ex)
            {
                if (!startup)
                    MessageBox.Show(ex.Message, "Quick Batch Snip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                instance.ReleaseMutex();
            }
        }
    }
}

internal sealed class SnipApp : ApplicationContext
{
    internal const int CaptureMessage = 0x8001;
    internal const int PasteMessage = 0x8002;
    private const int ShowStatusMessage = 0x8003;
    private const string WindowName = "QuickBatchSnip.69426C91.1.5";
    private static readonly IntPtr MessageWindows = new IntPtr(-3);
    private readonly List<Screenshot> queue = new List<Screenshot>();
    private readonly List<ClipboardBatch> fileBatches = new List<ClipboardBatch>();
    private readonly SnipWindow window;
    private readonly ShortcutHook keyboard;
    private readonly NotifyIcon tray;
    private readonly Icon icon;
    private readonly System.Windows.Forms.Timer timer;
    private readonly ContextMenuStrip menu;
    private readonly ToolStripMenuItem status;
    private readonly ToolStripMenuItem clear;
    private readonly ToolStripMenuItem combine;
    private readonly StatusWindow dashboard;
    private ClipboardBatch activeBatch;
    private bool selecting;
    private bool confirmingExit;
    private bool reopenStatus;
    private bool disposed;

    public SnipApp(bool showWindow)
    {
        window = new SnipWindow(this);
        try { keyboard = new ShortcutHook(window.Handle); }
        catch
        {
            window.DestroyHandle();
            throw;
        }

        icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
        status = new ToolStripMenuItem { Enabled = false };
        clear = new ToolStripMenuItem("Clear queue", null, delegate { ClearQueue(); });
        combine = new ToolStripMenuItem("Paste batch as one image") { CheckOnClick = true };
        menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] {
            new ToolStripMenuItem("Open Quick Batch Snip", null, delegate { ShowStatus(); }),
            status, new ToolStripSeparator(), combine, clear,
            new ToolStripMenuItem("Exit", null, delegate { ConfirmExit(); })
        });
        dashboard = new StatusWindow(icon, ConfirmExit);
        tray = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.MouseDoubleClick += delegate { ShowStatus(); };
        UpdateStatus();

        timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += delegate { MaintainClipboard(); };
        timer.Start();
        ThreadPool.QueueUserWorkItem(delegate { ClipboardBatch.CleanStaleFiles(); });
        if (showWindow)
            ShowStatus();
    }

    internal static bool ShowRunningInstance()
    {
        // The first process may still be creating its message window.
        for (int attempt = 0; attempt < 10; attempt++)
        {
            IntPtr running = Native.FindWindowEx(MessageWindows, IntPtr.Zero, null, WindowName);
            if (running != IntPtr.Zero)
            {
                uint processId;
                if (Native.GetWindowThreadProcessId(running, out processId) != 0)
                    Native.AllowSetForegroundWindow(processId);
                if (Native.PostMessage(running, ShowStatusMessage, IntPtr.Zero, IntPtr.Zero))
                    return true;
            }
            Thread.Sleep(50);
        }
        return false;
    }

    private void ShowStatus()
    {
        if (disposed)
            return;
        if (selecting)
        {
            reopenStatus = true;
            return;
        }
        reopenStatus = false;
        if (dashboard.WindowState == FormWindowState.Minimized)
            dashboard.WindowState = FormWindowState.Normal;
        dashboard.RefreshStartupSetting();
        dashboard.Show();
        // Raise the existing window before requesting keyboard focus.
        const uint moveToTop = 0x0013; // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
        Native.SetWindowPos(dashboard.Handle, IntPtr.Zero, 0, 0, 0, 0, moveToTop);
        dashboard.Activate();
    }

    private void ConfirmExit()
    {
        if (confirmingExit)
            return;

        confirmingExit = true;
        try
        {
            string message = "Exit Quick Batch Snip?";
            if (queue.Count > 0)
                message += "\r\n\r\nQueued screenshots will be cleared.";
            if (MessageBox.Show(dashboard.Visible ? dashboard : null, message, "Quick Batch Snip",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                ExitThread();
        }
        finally { confirmingExit = false; }
    }

    internal void Capture()
    {
        if (selecting || confirmingExit)
            return;

        selecting = true;
        keyboard.PasteEnabled = false;
        try
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            using (var frozen = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics graphics = Graphics.FromImage(frozen))
                    graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

                Rectangle selection;
                using (var overlay = new SelectionOverlay(frozen, bounds))
                {
                    overlay.ShowDialog();
                    selection = overlay.Selection;
                }
                if (selection == new Rectangle(Point.Empty, frozen.Size))
                {
                    queue.Add(new Screenshot(frozen));
                }
                else if (!selection.IsEmpty)
                {
                    using (Bitmap crop = frozen.Clone(selection, PixelFormat.Format24bppRgb))
                        queue.Add(new Screenshot(crop));
                }
            }
        }
        catch (Exception ex)
        {
            // The screen may have changed or become unavailable during capture.
            if (!(ex is ExternalException || ex is ArgumentException))
                throw;
        }
        finally
        {
            selecting = false;
            UpdateStatus();
            if (reopenStatus)
                ShowStatus();
        }
    }

    internal void Paste(IntPtr destination)
    {
        if (selecting || !StillFocused(destination))
            return;

        if (queue.Count == 0)
        {
            if (ClearActiveClipboard())
                ShortcutHook.ReplayPaste();
            return;
        }

        ClipboardBatch next = null;
        try
        {
            next = new ClipboardBatch(queue, combine.Checked);
            if (!StillFocused(destination) || !OpenClipboardWithRetry())
                return;

            try
            {
                if (!StillFocused(destination) || !Native.EmptyClipboard())
                    return;

                // EmptyClipboard notifies the previous owner before assigning ownership.
                DropActiveBatch();
                activeBatch = next;
                next = null;
                activeBatch.PublishFormats();
            }
            finally
            {
                Native.CloseClipboard();
            }

            activeBatch.Armed = true;
            if (!StillFocused(destination) || !ShortcutHook.ReplayPaste())
                ClearActiveClipboard();
        }
        catch (Exception ex)
        {
            // Keep the queue available if the transfer could not be prepared.
            if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ExternalException || ex is ArgumentException))
                throw;
        }
        finally
        {
            if (next != null)
                TrackFilesForCleanup(next);
            UpdateStatus();
        }
    }

    private static bool StillFocused(IntPtr destination)
    {
        return destination != IntPtr.Zero && Native.GetForegroundWindow() == destination;
    }

    internal void Render(uint format)
    {
        ClipboardBatch batch = activeBatch;
        if (batch == null || !batch.Armed || !batch.RenderFormat(format))
            return;

        // Give the recipient time to finish related clipboard reads.
        batch.ClipboardExpires = DateTime.UtcNow.AddSeconds(4);
        if (batch.Consumed)
            return;

        batch.Consumed = true;
        batch.FileExpires = DateTime.UtcNow.AddMinutes(10);
        queue.RemoveRange(0, batch.Count);
        UpdateStatus();
    }

    internal void DropActiveBatch()
    {
        if (activeBatch == null)
            return;
        TrackFilesForCleanup(activeBatch);
        activeBatch = null;
        UpdateStatus();
    }

    private void TrackFilesForCleanup(ClipboardBatch batch)
    {
        if (batch.Folder != null)
        {
            if (!batch.Consumed)
                batch.FileExpires = DateTime.UtcNow;
            fileBatches.Add(batch);
        }
    }

    private bool ClearActiveClipboard()
    {
        if (activeBatch == null)
            return true;

        if (Native.GetClipboardOwner() != window.Handle)
        {
            DropActiveBatch();
            return true;
        }
        if (!OpenClipboardWithRetry())
            return false;
        try
        {
            if (Native.GetClipboardOwner() == window.Handle && !Native.EmptyClipboard())
                return false;
            DropActiveBatch();
            return true;
        }
        finally { Native.CloseClipboard(); }
    }

    private bool OpenClipboardWithRetry()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (Native.OpenClipboard(window.Handle))
                return true;
            Thread.Sleep(12);
        }
        return false;
    }

    private void MaintainClipboard()
    {
        DateTime now = DateTime.UtcNow;
        if (activeBatch != null && now >= activeBatch.ClipboardExpires)
            ClearActiveClipboard();
        fileBatches.RemoveAll(batch => now >= batch.FileExpires && ClipboardBatch.DeleteFiles(batch.Folder));
    }

    private void ClearQueue()
    {
        if (activeBatch != null)
            activeBatch.Armed = false;
        queue.Clear();
        ClearActiveClipboard();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        keyboard.PasteEnabled = !selecting && (queue.Count > 0 || activeBatch != null);
        string count = queue.Count == 1 ? "1 screenshot queued" : queue.Count + " screenshots queued";
        dashboard.SetCount(count);
        status.Text = count + "  |  Win+S";
        tray.Text = "Quick Batch Snip · " + count;
        clear.Enabled = queue.Count > 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            timer.Dispose();
            keyboard.Dispose();
            ClearQueue();
            foreach (ClipboardBatch batch in fileBatches)
                ClipboardBatch.DeleteFiles(batch.Folder);
            tray.Dispose();
            menu.Dispose();
            dashboard.Dispose();
            icon.Dispose();
            window.DestroyHandle();
        }
        base.Dispose(disposing);
    }

    private sealed class SnipWindow : NativeWindow
    {
        private readonly SnipApp app;
        public SnipWindow(SnipApp app)
        {
            this.app = app;
            CreateHandle(new CreateParams { Parent = MessageWindows, Caption = WindowName });
        }

        protected override void WndProc(ref Message message)
        {
            switch (message.Msg)
            {
                case CaptureMessage:
                    app.Capture();
                    return;
                case PasteMessage:
                    app.Paste(message.WParam);
                    return;
                case ShowStatusMessage:
                    app.ShowStatus();
                    return;
                case 0x0305: // WM_RENDERFORMAT
                    app.Render(unchecked((uint)message.WParam.ToInt64()));
                    return;
                case 0x0307: // WM_DESTROYCLIPBOARD
                    app.DropActiveBatch();
                    return;
            }
            base.WndProc(ref message);
        }
    }
}

internal sealed class SelectionOverlay : Form
{
    private readonly Bitmap frozen;
    private readonly Rectangle screenBounds;
    private readonly FlowLayoutPanel prompt;
    private Point anchor;
    private bool dragging;
    private bool wasActivated;
    internal Rectangle Selection { get; private set; }

    public SelectionOverlay(Bitmap frozen, Rectangle screenBounds)
    {
        this.frozen = frozen;
        this.screenBounds = screenBounds;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        Bounds = screenBounds;

        prompt = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            BackColor = Color.FromArgb(242, 250, 247),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8, 5, 8, 5),
            Font = SystemFonts.MessageBoxFont
        };
        prompt.MouseDown += BeginDragFromPrompt;
        prompt.Controls.Add(PromptLabel("Drag to select a region"));
        var fullScreen = new Button
        {
            Name = "fullScreenButton",
            Text = "Full screen (Enter)",
            AutoSize = true,
            MinimumSize = new Size(0, 28),
            BackColor = Color.FromArgb(150, 242, 202),
            ForeColor = Color.FromArgb(12, 73, 56),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        fullScreen.FlatAppearance.BorderColor = Color.FromArgb(91, 162, 137);
        fullScreen.Click += delegate { CaptureFullScreen(); };
        prompt.Controls.Add(fullScreen);
        prompt.Controls.Add(PromptLabel("Esc to cancel"));
        Controls.Add(prompt);
        AcceptButton = fullScreen;
    }

    private Label PromptLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.FromArgb(21, 43, 51),
            Margin = new Padding(6, 9, 6, 3)
        };
        label.MouseDown += BeginDragFromPrompt;
        return label;
    }

    private void BeginDragFromPrompt(object sender, MouseEventArgs e)
    {
        Point point = PointToClient(((Control)sender).PointToScreen(e.Location));
        OnMouseDown(new MouseEventArgs(e.Button, e.Clicks, point.X, point.Y, e.Delta));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetWindowPos(Handle, IntPtr.Zero, screenBounds.X, screenBounds.Y,
            screenBounds.Width, screenBounds.Height, 0x0214); // no owner order, activate or z order
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Rectangle monitor = Rectangle.Intersect(screenBounds, Screen.FromPoint(Cursor.Position).WorkingArea);
        if (monitor.IsEmpty)
            monitor = screenBounds;
        Size size = prompt.GetPreferredSize(Size.Empty);
        prompt.Location = new Point(monitor.Left - screenBounds.Left + Math.Max(0, (monitor.Width - size.Width) / 2),
            monitor.Top - screenBounds.Top + 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(frozen, Point.Empty);
        if (!Selection.IsEmpty)
        {
            e.Graphics.DrawRectangle(Pens.Black, Selection.X, Selection.Y, Selection.Width - 1, Selection.Height - 1);
            if (Selection.Width > 2 && Selection.Height > 2)
                e.Graphics.DrawRectangle(Pens.White, Selection.X + 1, Selection.Y + 1, Selection.Width - 3, Selection.Height - 3);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            Cancel();
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            anchor = e.Location;
            dragging = true;
            Capture = true;
            prompt.Hide();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging)
            UpdateSelection(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!dragging || e.Button != MouseButtons.Left)
            return;

        UpdateSelection(e.Location);
        dragging = false;
        Capture = false;
        if (Selection.IsEmpty)
            prompt.Show();
        else
            FinishSelection(Selection);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter)
        {
            CaptureFullScreen();
            return true;
        }
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            Cancel();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        wasActivated = true;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (wasActivated && Visible && DialogResult == DialogResult.None)
            Cancel();
    }

    private void Cancel()
    {
        Selection = Rectangle.Empty;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void CaptureFullScreen()
    {
        FinishSelection(new Rectangle(Point.Empty, frozen.Size));
    }

    private void FinishSelection(Rectangle area)
    {
        Selection = area;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateSelection(Point cursor)
    {
        Rectangle oldSelection = Selection;
        Rectangle area = Rectangle.Intersect(ClientRectangle, Rectangle.FromLTRB(
            Math.Min(anchor.X, cursor.X), Math.Min(anchor.Y, cursor.Y),
            Math.Max(anchor.X, cursor.X), Math.Max(anchor.Y, cursor.Y)));
        Selection = area.Width > 0 && area.Height > 0 ? area : Rectangle.Empty;
        if (!oldSelection.IsEmpty)
            Invalidate(oldSelection);
        if (!Selection.IsEmpty)
            Invalidate(Selection);
    }
}

internal sealed class Screenshot
{
    internal readonly byte[] Png;
    internal readonly Size Size;

    internal Screenshot(Bitmap bitmap)
    {
        Size = bitmap.Size;
        Png = EncodePng(bitmap);
    }

    internal static byte[] EncodePng(Bitmap bitmap)
    {
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    internal static Bitmap Combine(IList<Screenshot> shots)
    {
        int width = 1;
        double height = 0;
        const int gap = 6;
        foreach (Screenshot shot in shots)
        {
            width = Math.Max(width, shot.Size.Width);
            height += shot.Size.Height + gap;
        }
        height -= gap;

        // Bound the dimensions and memory used by very large batches.
        double scale = Math.Min(1, 16000 / Math.Max(width, height));
        scale = Math.Min(scale, Math.Sqrt(24000000 / (width * height)));
        var sheet = new Bitmap(Math.Max(1, (int)Math.Ceiling(width * scale)),
            Math.Max(1, (int)Math.Ceiling(height * scale)), PixelFormat.Format24bppRgb);
        try
        {
            using (Graphics graphics = Graphics.FromImage(sheet))
            {
                graphics.Clear(Color.FromArgb(32, 32, 32));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                double top = 0;
                foreach (Screenshot shot in shots)
                {
                    using (var source = new MemoryStream(shot.Png))
                    using (var image = Image.FromStream(source))
                        graphics.DrawImage(image, new RectangleF(0, (float)(top * scale),
                            (float)(shot.Size.Width * scale), (float)(shot.Size.Height * scale)));
                    top += shot.Size.Height + gap;
                }
            }
            return sheet;
        }
        catch
        {
            sheet.Dispose();
            throw;
        }
    }

    internal static byte[] ToDib(byte[] png)
    {
        using (var stream = new MemoryStream(png))
        using (var image = new Bitmap(stream))
        {
            Rectangle area = new Rectangle(Point.Empty, image.Size);
            int rowSize = image.Width * 3;
            int stride = (rowSize + 3) & ~3;
            const int headerSize = 40;
            byte[] dib = new byte[checked(headerSize + stride * image.Height)];
            using (var writer = new BinaryWriter(new MemoryStream(dib)))
            {
                writer.Write(headerSize);
                writer.Write(image.Width);
                writer.Write(image.Height);
                writer.Write((short)1);
                writer.Write((short)24);
            }
            BitmapData data = image.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                for (int row = 0; row < image.Height; row++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), dib,
                        headerSize + (image.Height - 1 - row) * stride, rowSize);
            }
            finally { image.UnlockBits(data); }
            return dib;
        }
    }
}

internal sealed class ClipboardBatch
{
    private const uint DibFormat = 8;
    private const uint DropFormat = 15;
    private const string TempPrefix = "QuickBatchSnip-";
    private static readonly uint PngFormat = Native.RegisterClipboardFormat("PNG");
    private static readonly uint HistoryFormat = Native.RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private static readonly uint CloudFormat = Native.RegisterClipboardFormat("CanUploadToCloudClipboard");
    private byte[] png;
    private byte[] dib;
    private byte[] dropList;
    internal readonly int Count;
    internal readonly string Folder;
    internal bool Armed;
    internal bool Consumed;
    internal DateTime ClipboardExpires = DateTime.UtcNow.AddSeconds(8);
    internal DateTime FileExpires;

    internal ClipboardBatch(IList<Screenshot> shots, bool combine)
    {
        Count = shots.Count;
        if (Count == 1)
        {
            png = shots[0].Png;
        }
        else if (combine)
        {
            using (Bitmap sheet = Screenshot.Combine(shots))
                png = Screenshot.EncodePng(sheet);
        }
        else
        {
            Folder = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            try
            {
                var paths = new List<string>(Count);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                for (int index = 0; index < Count; index++)
                {
                    string name = string.Format(CultureInfo.InvariantCulture, "Screenshot-{0}-{1:D2}.png", stamp, index + 1);
                    string path = Path.Combine(Folder, name);
                    File.WriteAllBytes(path, shots[index].Png);
                    paths.Add(path);
                }
                // DROPFILES header followed by a double-null-terminated Unicode list.
                byte[] names = Encoding.Unicode.GetBytes(string.Join("\0", paths) + "\0\0");
                dropList = new byte[20 + names.Length];
                dropList[0] = 20;
                dropList[16] = 1;
                Buffer.BlockCopy(names, 0, dropList, 20, names.Length);
            }
            catch
            {
                DeleteFiles(Folder);
                throw;
            }
        }
        if (png != null)
            dib = Screenshot.ToDib(png);
    }

    internal void PublishFormats()
    {
        if (HistoryFormat != 0)
            WriteFormat(HistoryFormat, new byte[4]);
        if (CloudFormat != 0)
            WriteFormat(CloudFormat, new byte[4]);
        if (dropList != null)
            Native.SetClipboardData(DropFormat, IntPtr.Zero);
        else
        {
            if (PngFormat != 0)
                Native.SetClipboardData(PngFormat, IntPtr.Zero);
            Native.SetClipboardData(DibFormat, IntPtr.Zero);
        }
    }

    internal bool RenderFormat(uint format)
    {
        if (format == DropFormat && dropList != null)
            return WriteFormat(format, dropList);
        if (format == PngFormat && png != null)
            return WriteFormat(format, png);
        if (format == DibFormat && dib != null)
            return WriteFormat(format, dib);
        return false;
    }

    private static bool WriteFormat(uint format, byte[] data)
    {
        IntPtr memory = Native.GlobalAlloc(2, new UIntPtr((uint)data.Length)); // GMEM_MOVEABLE
        if (memory == IntPtr.Zero)
            return false;

        try
        {
            IntPtr bytes = Native.GlobalLock(memory);
            if (bytes == IntPtr.Zero)
                return false;
            try { Marshal.Copy(data, 0, bytes, data.Length); }
            finally { Native.GlobalUnlock(memory); }

            if (Native.SetClipboardData(format, memory) == IntPtr.Zero)
                return false;
            memory = IntPtr.Zero; // Windows now owns the buffer.
            return true;
        }
        finally
        {
            if (memory != IntPtr.Zero)
                Native.GlobalFree(memory);
        }
    }

    internal static void CleanStaleFiles()
    {
        try
        {
            foreach (string folder in Directory.EnumerateDirectories(Path.GetTempPath(), TempPrefix + "*"))
            {
                string name = Path.GetFileName(folder);
                Guid token;
                if (name.StartsWith(TempPrefix, StringComparison.Ordinal) &&
                    Guid.TryParseExact(name.Substring(TempPrefix.Length), "N", out token) &&
                    Directory.GetLastWriteTimeUtc(folder) < DateTime.UtcNow.AddMinutes(-15))
                    DeleteFiles(folder);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static bool DeleteFiles(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return true;
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                return true;
            foreach (string path in Directory.EnumerateFiles(folder, "Screenshot-*.png"))
                File.Delete(path);
            Directory.Delete(folder);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
