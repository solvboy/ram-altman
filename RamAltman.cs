// Ram Altman - a parody RAM "borrower".
// Single file, no settings, no registry, no temp files, no network. Nothing is ever written to disk.
// Build: build.bat  (uses the C# compiler that ships with Windows)
// License: MIT - free to use, edit and share. See LICENSE. (sam.jpg is licensed separately, see CREDITS.txt.)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Ram Altman")]
[assembly: AssemblyDescription("Parody app. Slowly fills RAM, asks consent before taking more, always leaves a reserve. Backspace quits instantly.")]
[assembly: AssemblyProduct("Ram Altman")]
[assembly: AssemblyCompany("solboy")]
[assembly: AssemblyCopyright("Copyright (c) 2026 solboy. MIT License.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace RamAltman
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetPhysicallyInstalledSystemMemory(out long totalKilobytes);

        // Physical RAM Windows can use, and how much of it is free right now.
        public static void ReadMemory(out long total, out long available)
        {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref m)) { total = 0; available = 0; return; }
            total = (long)m.ullTotalPhys;
            available = (long)m.ullAvailPhys;
        }

        // RAM physically installed (the number on the box, e.g. 32 GB).
        public static long InstalledBytes(long fallback)
        {
            try
            {
                long kb;
                if (GetPhysicallyInstalledSystemMemory(out kb) && kb > 0)
                {
                    long bytes = kb * 1024L;
                    if (bytes >= fallback) return bytes;
                }
            }
            catch { }
            return fallback;
        }
    }

    static class Panic
    {
        // Instant, unconditional death. No cleanup handlers, no dumps, no saved state.
        public static void Now()
        {
            try { Process.GetCurrentProcess().Kill(); } catch { }
            Environment.Exit(0);
        }
    }

    // Any window deriving from this dies the moment Backspace is pressed.
    class PanicForm : Form
    {
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Back)
            {
                Panic.Now();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(20, 22, 26);
        public static readonly Color Panel = Color.FromArgb(34, 38, 44);
        public static readonly Color Line = Color.FromArgb(58, 64, 74);
        public static readonly Color Text = Color.FromArgb(232, 234, 237);
        public static readonly Color Dim = Color.FromArgb(150, 157, 168);
        public static readonly Color Accent = Color.FromArgb(16, 163, 127);
        public static readonly Color Other = Color.FromArgb(96, 104, 118);
        public static readonly Color Danger = Color.FromArgb(235, 96, 84);

        public static string GB(long bytes)
        {
            return (bytes / 1073741824.0).ToString("0.0") + " GB";
        }
    }

    // Loads the same file into memory over and over, at a gentle pace, until told to stop.
    sealed class RamEater
    {
        const int ChunkBytes = 4 * 1024 * 1024;

        readonly List<byte[]> hoard = new List<byte[]>();
        readonly string payloadPath;
        readonly long bytesPerSecond;

        long held;
        long loads;
        long targetUsed;
        long maxHeld;
        volatile bool eating;
        volatile bool restart;
        volatile bool reached;
        volatile bool refused;

        public RamEater(string payloadPath, long bytesPerSecond)
        {
            this.payloadPath = payloadPath;
            this.bytesPerSecond = Math.Max(bytesPerSecond, ChunkBytes);
            Thread t = new Thread(Work);
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        public long Held { get { return Interlocked.Read(ref held); } }
        public long Loads { get { return Interlocked.Read(ref loads); } }
        public bool Refused { get { return refused; } }

        // Eat until system-wide usage hits targetUsedBytes, never holding more than maxHeldBytes ourselves.
        public void Begin(long targetUsedBytes, long maxHeldBytes)
        {
            Interlocked.Exchange(ref targetUsed, targetUsedBytes);
            Interlocked.Exchange(ref maxHeld, maxHeldBytes);
            reached = false;
            restart = true;
            eating = true;
        }

        // True once (per Begin) when the target has been reached.
        public bool TakeReached()
        {
            if (!reached) return false;
            reached = false;
            return true;
        }

        void Work()
        {
            Stopwatch clock = new Stopwatch();
            long sessionBytes = 0;
            FileStream file = null;

            while (true)
            {
                if (!eating) { Thread.Sleep(30); continue; }
                if (restart)
                {
                    restart = false;
                    sessionBytes = 0;
                    clock.Restart();
                }

                long total, avail;
                Native.ReadMemory(out total, out avail);
                long used = total - avail;
                if (used >= Interlocked.Read(ref targetUsed) || Held >= Interlocked.Read(ref maxHeld))
                {
                    eating = false;
                    reached = true;
                    continue;
                }

                // Pace ourselves: only allocate what the clock says we are allowed so far.
                long allowed = (long)(clock.Elapsed.TotalSeconds * bytesPerSecond);
                if (sessionBytes >= allowed) { Thread.Sleep(5); continue; }

                try
                {
                    if (file == null)
                    {
                        file = new FileStream(payloadPath, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete, 65536);
                    }
                    byte[] chunk = new byte[ChunkBytes];
                    int offset = 0;
                    int emptyReads = 0;
                    while (offset < chunk.Length)
                    {
                        int n = file.Read(chunk, offset, chunk.Length - offset);
                        if (n <= 0)
                        {
                            // Reached the end of the file: load it again from the top.
                            file.Position = 0;
                            Interlocked.Increment(ref loads);
                            if (++emptyReads > 1) break; // empty file, nothing to load
                            continue;
                        }
                        emptyReads = 0;
                        offset += n;
                    }
                    lock (hoard) { hoard.Add(chunk); }
                    Interlocked.Add(ref held, chunk.Length);
                    sessionBytes += chunk.Length;
                }
                catch (Exception)
                {
                    // Windows said no (out of memory, file vanished...). Stop where we are.
                    refused = true;
                    eating = false;
                    reached = true;
                }
            }
        }
    }

    sealed class PortraitBox : Control
    {
        Image portrait;

        public PortraitBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public Image Portrait
        {
            get { return portrait; }
            set { portrait = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Rectangle r = ClientRectangle;

            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, r);

            if (portrait != null)
            {
                // Fill the square, cropping the overflow (biased upward so faces stay in frame).
                float scale = Math.Max((float)r.Width / portrait.Width, (float)r.Height / portrait.Height);
                float sw = r.Width / scale, sh = r.Height / scale;
                RectangleF src = new RectangleF((portrait.Width - sw) / 2f, (portrait.Height - sh) * 0.25f, sw, sh);
                g.DrawImage(portrait, r, src, GraphicsUnit.Pixel);
            }
            else
            {
                float w = r.Width, h = r.Height;
                using (SolidBrush b = new SolidBrush(Theme.Line))
                {
                    g.FillEllipse(b, w * 0.33f, h * 0.20f, w * 0.34f, h * 0.38f);
                    g.FillEllipse(b, w * 0.12f, h * 0.64f, w * 0.76f, h * 0.80f);
                }
                using (Font f = new Font("Segoe UI", 8.5f))
                using (SolidBrush b = new SolidBrush(Theme.Text))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Far;
                    RectangleF tr = new RectangleF(4, 0, w - 8, h - 8);
                    g.DrawString("put sam.jpg next to RamAltman.exe", f, b, tr, sf);
                }
            }

            using (Pen p = new Pen(Theme.Accent, 2f))
                g.DrawRectangle(p, 1, 1, r.Width - 2, r.Height - 2);
        }
    }

    // One bar for the whole machine: everything else | Ram Altman's share | free, with the two limits marked.
    sealed class MemoryBar : Control
    {
        public long Total, Used, Held, ConsentAt, Ceiling;

        public MemoryBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.Back)) g.FillRectangle(b, ClientRectangle);
            if (Total <= 0) return;

            int w = ClientSize.Width;
            int labelH = Font.Height + 4;
            int barH = ClientSize.Height - labelH;
            Rectangle bar = new Rectangle(0, 0, w, barH);

            long held = Math.Min(Held, Used);
            long other = Math.Max(0, Used - held);
            int xOther = (int)(w * (double)other / Total);
            int xUsed = (int)(w * (double)Math.Min(Used, Total) / Total);

            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, bar);
            using (SolidBrush b = new SolidBrush(Theme.Other)) g.FillRectangle(b, 0, 0, xOther, barH);
            using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, xOther, 0, Math.Max(0, xUsed - xOther), barH);

            DrawMark(g, w, barH, ConsentAt, "ask first", Theme.Text);
            DrawMark(g, w, barH, Ceiling, "hard stop", Theme.Danger);
        }

        void DrawMark(Graphics g, int w, int barH, long at, string label, Color color)
        {
            if (at <= 0) return;
            int x = Math.Min(w - 1, (int)(w * (double)at / Total));
            using (Pen p = new Pen(color, 2f)) g.DrawLine(p, x, 0, x, barH + 2);
            Size sz = TextRenderer.MeasureText(label, Font);
            int tx = Math.Max(0, Math.Min(w - sz.Width, x - sz.Width / 2));
            TextRenderer.DrawText(g, label, Font, new Point(tx, barH + 3), color);
        }
    }

    sealed class ConsentForm : PanicForm
    {
        readonly CheckBox agree;
        readonly Button more;
        readonly DataGridView grid;

        public ConsentForm(string payloadName, long installed, long total, long used, long held, long ceiling)
        {
            long leftForYou = Math.Max(0, total - ceiling);
            long extra = Math.Max(0, ceiling - used);

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(680, 600);
            Text = "Ram Altman - Consent";
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            TopMost = true;

            Label title = new Label();
            title.Text = "Would you like to go more?";
            title.Font = new Font("Segoe UI Semibold", 15f);
            title.SetBounds(20, 14, 640, 34);

            Label sub = new Label();
            sub.Text = "I'm holding " + Theme.GB(held) + ". Say yes and I'll take about " + Theme.GB(extra) +
                       " more, stopping at " + Theme.GB(ceiling) + " in use. Read the fine print first.";
            sub.ForeColor = Theme.Dim;
            sub.SetBounds(20, 50, 640, 40);

            grid = new DataGridView();
            grid.SetBounds(20, 96, 640, 384);
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.BackgroundColor = Theme.Panel;
            grid.GridColor = Theme.Line;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Back;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Dim;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Back;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4);
            grid.DefaultCellStyle.BackColor = Theme.Panel;
            grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = Theme.Panel;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.DefaultCellStyle.Padding = new Padding(4, 5, 4, 5);
            AddColumn("No.", 9);
            AddColumn("Clause", 27);
            AddColumn("What it says", 64);

            Section("TERMS AND CONDITIONS");
            Row("1", "The loan", "You lend Ram Altman more of this computer's memory. It is borrowed, not consumed.");
            Row("2", "The 5% doctrine", "\"You're not going to use more than 5% of it.\" This is a sales pitch, not a measurement.");
            Row("3", "Hard stop", "Total memory in use will not be pushed past " + Theme.GB(ceiling) + ". About " + Theme.GB(leftForYou) + " always stays yours.");
            Row("4", "Revocation", "Press Backspace at any time. The app dies instantly and Windows takes every byte back.");
            Row("5", "Returns", "Closing the window also returns 100% of the RAM. There is nothing to uninstall or undo.");
            Row("6", "Side effects", "Other programs may slow down while I hold your RAM. Save your work before agreeing.");
            Row("7", "Warranty", "None. This is a joke app, provided as is.");
            Row("8", "Parody", "Not affiliated with, endorsed by, or connected to Sam Altman or OpenAI.");

            Section("PRIVACY POLICY");
            Row("9", "Data collected", "None.");
            Row("10", "Files written", "None. No settings, cache, logs or temp files. Every launch is a first launch.");
            Row("11", "Registry", "Never read, never written.");
            Row("12", "Network", "Never used. There is no networking code in this app.");
            Row("13", "What I read", "How much RAM you have, and one file (" + payloadName + ") loaded again and again as filler.");
            Row("14", "After exit", "Nothing of mine remains. Windows keeps its own launch records (e.g. Prefetch); those are not mine to control.");

            Section("RAM BUDGET");
            Row("15", "Installed", Theme.GB(installed) + " (" + Theme.GB(total) + " usable by Windows)");
            Row("16", "In use right now", Theme.GB(used) + ", of which I hold " + Theme.GB(held));
            Row("17", "If you consent", "I take about " + Theme.GB(extra) + " more, slowly.");
            Row("18", "Ceiling", Theme.GB(ceiling) + " in use, machine-wide");
            Row("19", "Left for you", Theme.GB(leftForYou));

            agree = new CheckBox();
            agree.Text = "I have read the above and I consent to Ram Altman taking more RAM.";
            agree.SetBounds(20, 490, 640, 26);
            agree.FlatStyle = FlatStyle.Flat;
            agree.CheckedChanged += delegate
            {
                more.Enabled = agree.Checked;
                more.BackColor = agree.Checked ? Theme.Accent : Theme.Panel;
            };

            Label panic = new Label();
            panic.Text = "Backspace = instant shutdown";
            panic.ForeColor = Theme.Danger;
            panic.TextAlign = ContentAlignment.MiddleLeft;
            panic.SetBounds(20, 540, 280, 36);

            Button enough = MakeButton("No, that's enough", Theme.Panel);
            enough.SetBounds(336, 540, 160, 38);
            enough.DialogResult = DialogResult.No;

            more = MakeButton("Take more", Theme.Panel);
            more.SetBounds(506, 540, 154, 38);
            more.DialogResult = DialogResult.Yes;
            more.Enabled = false;

            Controls.AddRange(new Control[] { title, sub, grid, agree, panic, enough, more });
            CancelButton = enough;
            ActiveControl = enough;
            ResumeLayout(false);
            PerformLayout();

            Shown += delegate { grid.ClearSelection(); enough.Focus(); };
        }

        void AddColumn(string header, float weight)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = header;
            c.FillWeight = weight;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(c);
        }

        void Row(string number, string clause, string says)
        {
            grid.Rows.Add(number, clause, says);
        }

        void Section(string name)
        {
            int i = grid.Rows.Add("", name, "");
            DataGridViewCellStyle s = grid.Rows[i].DefaultCellStyle;
            s.BackColor = Theme.Back;
            s.SelectionBackColor = Theme.Back;
            s.ForeColor = Theme.Accent;
            s.SelectionForeColor = Theme.Accent;
            s.Font = new Font("Segoe UI Semibold", 9f);
        }

        static Button MakeButton(string text, Color back)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.Line;
            b.BackColor = back;
            b.ForeColor = Theme.Text;
            b.UseVisualStyleBackColor = false;
            return b;
        }
    }

    sealed class MainForm : PanicForm
    {
        enum Phase { Detecting, EatingToAsk, AwaitingConsent, EatingToCeiling, Full, Declined, Refused }

        const string Quote = "“Well, you're not going to use more than 5% of it, so let me have it.”";

        readonly Label installedLabel, usedLabel, heldLabel, fileLabel, statusLabel;
        readonly MemoryBar bar;
        readonly System.Windows.Forms.Timer timer;
        readonly Stopwatch uptime = Stopwatch.StartNew();
        readonly RamEater eater;
        readonly string payloadName;
        readonly long installed, total, askAt, ceiling, maxHeld;
        MemoryStream portraitData; // must outlive the Image created from it
        Phase phase = Phase.Detecting;

        public MainForm()
        {
            long avail;
            Native.ReadMemory(out total, out avail);
            installed = Native.InstalledBytes(total);

            // Leave room: one eighth of the machine (32 GB -> stop at 28 GB), and never less than 2 GB.
            long reserve = Math.Max(installed / 8, 2L * 1024 * 1024 * 1024);
            ceiling = Math.Max(0, Math.Min(installed - reserve, total - reserve / 2));
            askAt = Math.Min((long)(total * 0.55), ceiling);
            // Runaway guard on our own share. Slack covers other programs shrinking while we grow,
            // so the machine-wide ceiling is normally what stops us.
            maxHeld = Math.Min(ceiling, Math.Max(0, ceiling - (total - avail) + reserve / 2));

            string dir = AppDomain.CurrentDomain.BaseDirectory;
            string portraitPath = FindPortrait(dir);
            string payloadPath = portraitPath != null ? portraitPath : Application.ExecutablePath;
            payloadName = Path.GetFileName(payloadPath);

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(460, 632);
            Text = "Ram Altman";
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            PortraitBox portrait = new PortraitBox();
            portrait.SetBounds(130, 22, 200, 200);
            portrait.Portrait = LoadPortrait(portraitPath);

            Label name = new Label();
            name.Text = "RAM ALTMAN";
            name.Font = new Font("Segoe UI Semibold", 10f);
            name.ForeColor = Theme.Accent;
            name.TextAlign = ContentAlignment.MiddleCenter;
            name.SetBounds(20, 228, 420, 24);

            Label quote = new Label();
            quote.Text = Quote;
            quote.Font = new Font("Segoe UI", 12.5f, FontStyle.Italic);
            quote.BackColor = Theme.Panel;
            quote.TextAlign = ContentAlignment.MiddleCenter;
            quote.Padding = new Padding(12, 6, 12, 6);
            quote.SetBounds(20, 258, 420, 78);

            installedLabel = StatLabel(352);
            usedLabel = StatLabel(378);
            heldLabel = StatLabel(404);
            heldLabel.ForeColor = Theme.Accent;
            heldLabel.Font = new Font("Segoe UI Semibold", 10.5f);
            fileLabel = StatLabel(430);
            fileLabel.ForeColor = Theme.Dim;

            bar = new MemoryBar();
            bar.Font = new Font("Segoe UI", 8f);
            bar.SetBounds(20, 466, 420, 46);
            bar.Total = total;
            bar.ConsentAt = askAt;
            bar.Ceiling = ceiling;

            statusLabel = new Label();
            statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            statusLabel.SetBounds(20, 520, 420, 44);

            Label panic = new Label();
            panic.Text = "PANIC BUTTON: Backspace. Instant shutdown, nothing saved, ever.";
            panic.ForeColor = Theme.Danger;
            panic.BackColor = Theme.Panel;
            panic.TextAlign = ContentAlignment.MiddleCenter;
            panic.SetBounds(0, 588, 460, 44);

            Controls.AddRange(new Control[] { portrait, name, quote, installedLabel, usedLabel, heldLabel,
                                              fileLabel, bar, statusLabel, panic });
            ResumeLayout(false);
            PerformLayout();

            // About half a percent of the machine per second: slow enough to watch.
            eater = new RamEater(payloadPath, total / 200);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 100;
            timer.Tick += delegate { Tick(); };
            timer.Start();
            Tick();
        }

        Label StatLabel(int y)
        {
            Label l = new Label();
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.SetBounds(20, y, 420, 24);
            return l;
        }

        static string FindPortrait(string dir)
        {
            string[] names = { "sam.jpg", "sam.jpeg", "sam.png", "sam.bmp", "sam.gif" };
            foreach (string n in names)
            {
                string p = Path.Combine(dir, n);
                try { if (File.Exists(p) && new FileInfo(p).Length > 0) return p; } catch { }
            }
            return null;
        }

        Image LoadPortrait(string path)
        {
            if (path == null) return null;
            try
            {
                portraitData = new MemoryStream(File.ReadAllBytes(path));
                return Image.FromStream(portraitData);
            }
            catch { return null; }
        }

        void Tick()
        {
            long t, avail;
            Native.ReadMemory(out t, out avail);
            long used = t - avail;
            long held = eater.Held;

            installedLabel.Text = "Your computer has " + Theme.GB(installed) + " of RAM";
            usedLabel.Text = "In use right now: " + Theme.GB(used) + "  (" + (int)Math.Round(100.0 * used / total) + "%)";
            heldLabel.Text = "Ram Altman is holding " + Theme.GB(held);
            fileLabel.Text = "Loaded " + payloadName + " " + eater.Loads.ToString("N0") + " times";
            bar.Used = used;
            bar.Held = held;
            bar.Invalidate();

            switch (phase)
            {
                case Phase.Detecting:
                    statusLabel.Text = "Counting your RAM...";
                    if (uptime.ElapsedMilliseconds > 2500)
                    {
                        phase = Phase.EatingToAsk;
                        eater.Begin(askAt, maxHeld);
                    }
                    break;

                case Phase.EatingToAsk:
                    statusLabel.Text = "Loading " + payloadName + " again. And again. And again.";
                    if (eater.TakeReached())
                    {
                        if (eater.Refused) phase = Phase.Refused;
                        else if (ceiling - askAt < 64L * 1024 * 1024) phase = Phase.Full;
                        else AskConsent(used, held);
                    }
                    break;

                case Phase.AwaitingConsent:
                    statusLabel.Text = "I'd like more. Waiting for your consent.";
                    break;

                case Phase.EatingToCeiling:
                    statusLabel.Text = "Consent received. Scaling up.";
                    if (eater.TakeReached()) phase = eater.Refused ? Phase.Refused : Phase.Full;
                    break;

                case Phase.Full:
                    statusLabel.Text = "That's my limit. I left you " + Theme.GB(Math.Max(0, total - ceiling)) +
                                       ". Close the window to get it all back.";
                    break;

                case Phase.Declined:
                    statusLabel.Text = "Fine. I'll just hold on to what I've got.\nClose the window to get it back.";
                    break;

                case Phase.Refused:
                    statusLabel.Text = "Windows won't give me any more. Holding here.";
                    break;
            }
        }

        void AskConsent(long used, long held)
        {
            phase = Phase.AwaitingConsent;
            DialogResult answer;
            using (ConsentForm dialog = new ConsentForm(payloadName, installed, total, used, held, ceiling))
                answer = dialog.ShowDialog(this);

            if (answer == DialogResult.Yes)
            {
                phase = Phase.EatingToCeiling;
                eater.Begin(ceiling, maxHeld);
            }
            else
            {
                phase = Phase.Declined;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Closing is the same as panicking: gone immediately, RAM handed straight back.
            Panic.Now();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
