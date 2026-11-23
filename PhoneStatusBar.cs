using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace PocketViewport;

internal sealed class PhoneStatusBar : Control
{
    private static readonly Color Ink = Color.FromArgb(229, 233, 242);
    private static readonly Color Muted = Color.FromArgb(144, 153, 171);
    private static readonly Color Accent = Color.FromArgb(166, 193, 247);
    private readonly Font clockFont = new("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font statusFont = new("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 5_000 };
    private readonly ContextMenuStrip notificationMenu = new() { ShowImageMargin = false };
    private readonly Button bell = new()
    {
        AccessibleName = "Notifications", FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
    };
    private readonly List<(DateTime Time, string Message)> notifications = [];
    private double scale = 1;
    private string time = "";
    private Connection connection;
    private PowerReading power;
    private bool unread;

    public PhoneStatusBar(Color background)
    {
        BackColor = background;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
        bell.BackColor = background;
        bell.FlatAppearance.BorderSize = 0;
        bell.Paint += PaintBell;
        bell.Click += (_, _) => ShowNotifications();
        Controls.Add(bell);
        refresh.Tick += (_, _) => RefreshStatus();
        RefreshStatus();
        refresh.Start();
    }

    public void SetScale(double value)
    {
        scale = value;
        bell.SetBounds((int)Math.Round(69 * scale), 1,
            (int)Math.Round(26 * scale), Math.Max(1, Height - 2));
        bell.Invalidate();
        Invalidate();
    }

    public void Notify(string message)
    {
        if (notifications.Count == 4)
            notifications.RemoveAt(0);
        notifications.Add((DateTime.Now, message));
        SetUnread(true);
    }

    private void SetUnread(bool value)
    {
        unread = value;
        bell.AccessibleName = value ? "Notifications, unread" : "Notifications";
        bell.Invalidate();
    }

    private void ShowNotifications()
    {
        while (notificationMenu.Items.Count > 0)
            notificationMenu.Items[0].Dispose();
        SetUnread(false);
        if (notifications.Count == 0)
        {
            notificationMenu.Items.Add(new ToolStripLabel("No notifications"));
        }
        else
        {
            for (int i = notifications.Count - 1; i >= 0; i--)
            {
                var entry = notifications[i];
                notificationMenu.Items.Add(new ToolStripLabel($"{entry.Time:t}  {entry.Message}"));
            }
            notificationMenu.Items.Add(new ToolStripSeparator());
            notificationMenu.Items.Add("Clear notifications", null, (_, _) => notifications.Clear());
        }
        notificationMenu.Show(bell, new Point(0, bell.Height));
    }

    private void RefreshStatus()
    {
        string nextTime = DateTime.Now.ToString("t");
        var nextConnection = ReadConnection();
        var nextPower = ReadPower();
        if (time == nextTime && connection == nextConnection && power == nextPower)
            return;
        time = nextTime;
        power = nextPower;
        connection = nextConnection;
        string network = connection switch
        {
            Connection.WiFi => "Wi-Fi connected",
            Connection.Network => "Network connected",
            _ => "Network disconnected"
        };
        string battery = power.Percent is { } percent ? $"Battery {percent} percent" : "Battery unavailable";
        AccessibleName = $"{time}. {network}. {battery}{(power.Charging ? ", charging" : "")}.";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform((float)scale, (float)scale);
        int width = (int)Math.Round(ClientSize.Width / scale);
        using var text = new SolidBrush(Ink);
        using var textLayout = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(time, clockFont, text, new RectangleF(0, 0, 69, 24), textLayout);

        int cameraLeft = (width - 58) / 2;
        using var camera = PhoneWindow.RoundedPath(new Rectangle(cameraLeft, 5, 58, 13), 7);
        using var recess = new SolidBrush(Color.FromArgb(6, 7, 10));
        using var lens = new SolidBrush(Color.FromArgb(61, 69, 101));
        g.FillPath(recess, camera);
        g.FillEllipse(lens, cameraLeft + 45, 9.5f, 4, 4);

        DrawConnection(g, width - 77);
        var chargeColor = power.Charging ? Color.FromArgb(140, 219, 172) : Ink;
        string charge = power.Percent is { } percent ? $"{percent}%" : power.OnAc ? "AC" : "–";
        textLayout.Alignment = StringAlignment.Far;
        using var chargeInk = new SolidBrush(chargeColor);
        g.DrawString(charge, statusFont, chargeInk, new RectangleF(width - 58, 0, 30, 24), textLayout);

        int batteryLeft = width - 23;
        using var battery = PhoneWindow.RoundedPath(new Rectangle(batteryLeft, 7, 20, 10), 2);
        using var batteryEdge = new Pen(Muted);
        using var terminal = new SolidBrush(Muted);
        g.DrawPath(batteryEdge, battery);
        g.FillRectangle(terminal, batteryLeft + 21, 10, 1.5f, 4);
        if (power.Percent is > 0)
            g.FillRectangle(chargeInk, batteryLeft + 2, 9, 16 * power.Percent.Value / 100f, 6);
    }

    private void DrawConnection(Graphics g, int x)
    {
        using var stroke = new Pen(connection == Connection.Offline ? Muted : Ink, 1.3f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (connection == Connection.WiFi)
        {
            using var dot = new SolidBrush(Ink);
            g.DrawArc(stroke, x, 7, 14, 13, 224, 92);
            g.DrawArc(stroke, x + 3, 10, 8, 8, 224, 92);
            g.FillEllipse(dot, x + 6, 14, 2, 2);
            return;
        }

        g.DrawRectangle(stroke, x + 1, 7, 12, 8);
        g.DrawLine(stroke, x + 4, 17, x + 10, 17);
        if (connection == Connection.Offline)
            g.DrawLine(stroke, x, 17, x + 14, 6);
    }

    private void PaintBell(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(bell.Width / 2f, bell.Height / 2f);
        g.ScaleTransform((float)scale, (float)scale);
        using var stroke = new Pen(unread ? Ink : Muted, 1.2f)
            { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var shape = new GraphicsPath();
        shape.AddBezier(-4, 3, -3, 1, -4, -4, 0, -5);
        shape.AddBezier(0, -5, 4, -4, 3, 1, 4, 3);
        shape.CloseFigure();
        g.DrawPath(stroke, shape);
        g.DrawLine(stroke, -1, 5, 1, 5);
        if (unread)
        {
            using var dot = new SolidBrush(Accent);
            g.FillEllipse(dot, 3, -7, 5, 5);
        }
        g.ResetTransform();
    }

    private enum Connection { Offline, WiFi, Network }

    private static Connection ReadConnection()
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
                return Connection.Offline;
            return NetworkInterface.GetAllNetworkInterfaces().Any(adapter =>
                adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                adapter.OperationalStatus == OperationalStatus.Up)
                ? Connection.WiFi : Connection.Network;
        }
        catch (NetworkInformationException)
        {
            return Connection.Offline;
        }
    }

    private readonly record struct PowerReading(int? Percent, bool Charging, bool OnAc);

    private static PowerReading ReadPower()
    {
        if (!GetSystemPowerStatus(out var status))
            return default;
        bool flagsKnown = status.BatteryFlag != byte.MaxValue;
        bool noBattery = flagsKnown && (status.BatteryFlag & 128) != 0;
        int? percent = !noBattery && status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null;
        bool charging = flagsKnown && (status.BatteryFlag & 8) != 0;
        return new PowerReading(percent, charging, status.AcLineStatus == 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refresh.Dispose();
            notificationMenu.Dispose();
            clockFont.Dispose();
            statusFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
