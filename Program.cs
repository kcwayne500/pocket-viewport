using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace PocketViewport;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        using var window = new PhoneWindow { Icon = icon };
        Application.Run(window);
    }
}

internal sealed class PhoneWindow : Form
{
    private sealed record Device(string Name, int Width, int Height, double PixelRatio,
        string UserAgent, object? ClientHints);

    private const int GalaxyWidth = 384;
    private const int GalaxyHeight = 854;
    private const int Bezel = 9;
    private const int TopBar = 30;
    private const int BottomBar = 39;
    private static readonly Color FrameColor = Color.FromArgb(19, 21, 27);

    private readonly Panel screen = new();
    private readonly PhoneStatusBar statusBar = new(FrameColor);
    private readonly Panel browser = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly LauncherView launcher = new() { Dock = DockStyle.Fill };
    private TextBox Address => launcher.Address;
    private Button OpenButton => launcher.OpenButton;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem devicesMenu = new("Device");
    private readonly ToolStripMenuItem back = new("Back");
    private readonly ToolStripMenuItem forward = new("Forward");
    private readonly ToolStripMenuItem reload = new("Reload");
    private readonly ToolStripMenuItem alwaysOnTop = new("Always on top");
    private readonly FrameButton frameBack = new("Back", FrameAction.Back) { Enabled = false };
    private readonly FrameButton frameHome = new("Open URL", FrameAction.Home);
    private readonly FrameButton frameMenu = new("App menu", FrameAction.Menu);
    private CoreWebView2Controller? controller;
    private RecentSites? recentSites;
    private CoreWebView2? Core => controller?.CoreWebView2;
    private Uri? CurrentPage => TryWebUri(Core?.Source ?? "", out var uri) ? uri : null;
    private Device[] devices = [];
    private int selectedDevice;
    private bool changingDevice;
    private bool initialNavigation;
    private double frameScale = 1;

    public PhoneWindow()
    {
        Text = "Pocket Viewport";
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        BackColor = FrameColor;
        ClientSize = new Size(GalaxyWidth + Bezel * 2, GalaxyHeight + TopBar + BottomBar);
        screen.Controls.Add(browser);
        screen.Controls.Add(launcher);
        Controls.AddRange([screen, statusBar, frameBack, frameHome, frameMenu]);
        frameBack.Click += (_, _) =>
        {
            if (launcher.Visible)
                ShowBrowser();
            else
                Core?.GoBack();
        };
        frameHome.Click += (_, _) => ShowLauncher();
        frameMenu.Click += (_, _) => menu.Show(frameMenu, Point.Empty, ToolStripDropDownDirection.AboveLeft);
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) MoveWindow(true); };
        statusBar.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) MoveWindow(true); };
        ContextMenuStrip = menu;
        OpenButton.Click += (_, _) => NavigateFromLauncher();
        launcher.RecentSiteSelected += uri =>
        {
            Address.Text = uri.AbsoluteUri;
            OpenWebsite(uri);
        };
        launcher.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) MoveWindow(true); };
        launcher.ContextMenuStrip = menu;
        BuildMenu();
        LayoutFrame();
        AcceptButton = OpenButton;
        Shown += async (_, _) =>
        {
            FitFrame(GalaxyWidth, GalaxyHeight);
            await InitializeAsync();
        };
        DpiChanged += (_, _) => BeginInvoke(async () =>
        {
            if (devices.Length > 0)
                await ApplyDeviceAsync(selectedDevice, false);
        });
    }

    private double FitFrame(int viewportWidth, int viewportHeight)
    {
        var area = Screen.FromControl(this).WorkingArea;
        double dpiScale = DeviceDpi / 96.0;
        double fit = Math.Min(1, Math.Min(
            area.Width / ((viewportWidth + Bezel * 2) * dpiScale),
            area.Height / ((viewportHeight + TopBar + BottomBar) * dpiScale)));
        frameScale = dpiScale * fit;
        ClientSize = new Size(Math.Min(area.Width, Pixels(viewportWidth + Bezel * 2)),
            Math.Min(area.Height, Pixels(viewportHeight + TopBar + BottomBar)));
        Location = new Point(Math.Clamp(Left, area.Left, area.Right - Width),
            Math.Clamp(Top, area.Top, area.Bottom - Height));
        LayoutFrame();
        return fit;
    }

    private int Pixels(int logical) => Math.Max(1, (int)Math.Round(logical * frameScale));

    private void LayoutFrame()
    {
        using var outline = RoundedPath(ClientRectangle, Pixels(35));
        Region = new Region(outline);
        int edge = Pixels(Bezel);
        int footer = Pixels(BottomBar);
        screen.Bounds = Rectangle.FromLTRB(edge, Pixels(TopBar), ClientSize.Width - edge, ClientSize.Height - footer);
        using var displayOutline = RoundedPath(screen.ClientRectangle, Pixels(10));
        screen.Region = new Region(displayOutline);

        int statusInset = edge + Pixels(15);
        statusBar.SetBounds(statusInset, Pixels(4), ClientSize.Width - statusInset * 2, Pixels(24));
        statusBar.SetScale(frameScale);

        int y = screen.Bottom + Pixels(3);
        int height = footer - Pixels(7);
        int wing = Pixels(65);
        int center = Pixels(142);
        frameBack.SetBounds(edge + Pixels(8), y, wing, height);
        frameHome.SetBounds((ClientSize.Width - center) / 2, y, center, height);
        frameMenu.SetBounds(ClientSize.Width - edge - Pixels(8) - wing, y, wing, height);
        foreach (var button in new[] { frameBack, frameHome, frameMenu })
            button.SetScale(frameScale);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var rim = new LinearGradientBrush(ClientRectangle, Color.FromArgb(110, 115, 130),
            Color.FromArgb(45, 48, 58), LinearGradientMode.ForwardDiagonal);
        using var rimPen = new Pen(rim, Pixels(2));
        var inset = Rectangle.Inflate(ClientRectangle, -Pixels(2), -Pixels(2));
        using var outline = RoundedPath(inset, Pixels(33));
        e.Graphics.DrawPath(rimPen, outline);
    }

    internal static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private enum FrameAction { Back, Home, Menu }

    private sealed class FrameButton : Button
    {
        private readonly FrameAction action;
        private double scale = 1;

        public FrameButton(string label, FrameAction action)
        {
            this.action = action;
            AccessibleName = label;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
        }

        public void SetScale(double value)
        {
            scale = value;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(FrameColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TranslateTransform(ClientSize.Width / 2f, ClientSize.Height / 2f);
            e.Graphics.ScaleTransform((float)scale, (float)scale);
            var color = Enabled ? Color.FromArgb(202, 207, 222) : Color.FromArgb(86, 91, 105);
            using var ink = new SolidBrush(color);
            using var line = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            switch (action)
            {
                case FrameAction.Back:
                    e.Graphics.DrawLines(line, [new PointF(2, -5), new PointF(-3, 0), new PointF(2, 5)]);
                    break;
                case FrameAction.Home:
                    using (var pill = RoundedPath(new Rectangle(-49, -2, 98, 4), 2))
                        e.Graphics.FillPath(ink, pill);
                    break;
                case FrameAction.Menu:
                    for (int x = -6; x <= 6; x += 6)
                        e.Graphics.FillEllipse(ink, x - 1.5f, -1.5f, 3, 3);
                    break;
            }
            e.Graphics.ResetTransform();
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3), color, FrameColor);
        }
    }

    private void BuildMenu()
    {
        menu.Items.Add("Open URL   Ctrl+L", null, (_, _) => ShowLauncher());
        menu.Items.AddRange([back, forward, reload, new ToolStripSeparator(), devicesMenu, alwaysOnTop]);
        back.Click += (_, _) => Core?.GoBack();
        forward.Click += (_, _) => Core?.GoForward();
        reload.Click += (_, _) => Core?.Reload();
        alwaysOnTop.Click += (_, _) => TopMost = !TopMost;
        menu.Items.Add("Move window", null, (_, _) => BeginInvoke(() => MoveWindow(false)));
        menu.Items.Add("Close", null, (_, _) => Close());
        menu.Opening += (_, _) =>
        {
            back.Enabled = Core?.CanGoBack == true;
            forward.Enabled = Core?.CanGoForward == true;
            reload.Enabled = CurrentPage is not null;
            alwaysOnTop.Checked = TopMost;
        };
    }

    private async Task InitializeAsync()
    {
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketViewport");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: data);
            recentSites = new RecentSites(environment.UserDataFolder);
            launcher.SetRecentSites(recentSites.Entries);
            controller = await environment.CreateCoreWebView2ControllerAsync(browser.Handle);
            controller.IsVisible = false;
            controller.Bounds = browser.ClientRectangle;
            browser.Resize += (_, _) => controller.Bounds = browser.ClientRectangle;
            LocationChanged += (_, _) => controller.NotifyParentWindowPositionChanged();
            var core = controller.CoreWebView2;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.ContextMenuRequested += (_, e) =>
            {
                e.Handled = true;
                var location = e.Location;
                BeginInvoke(() => menu.Show(browser, location));
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (TryWebUri(e.Uri, out var uri))
                    BeginInvoke(() => core.Navigate(uri.AbsoluteUri));
            };
            core.NavigationStarting += (sender, e) =>
            {
                if (!TryWebUri(e.Uri, out _))
                    e.Cancel = true;
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (!initialNavigation || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
                    return;
                initialNavigation = false;
                if (!e.IsSuccess)
                {
                    statusBar.Notify("Page couldn’t load");
                    ShowLauncher($"Couldn’t load the page ({e.WebErrorStatus}).");
                }
            };
            core.HistoryChanged += (_, _) => UpdateFrameBack();
            controller.AcceleratorKeyPressed += (_, e) =>
            {
                if (e.KeyEventKind is not (CoreWebView2KeyEventKind.KeyDown or CoreWebView2KeyEventKind.SystemKeyDown))
                    return;
                if (Shortcut((Keys)e.VirtualKey | ModifierKeys) is { } action)
                {
                    e.Handled = true;
                    BeginInvoke(action);
                }
            };

            var version = environment.BrowserVersionString.Split(' ')[0];
            const string android = "Linux; Android 10; K";
            devices =
            [
                new("Galaxy · 384 × 854", GalaxyWidth, GalaxyHeight, 3,
                    $"Mozilla/5.0 ({android}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{version.Split('.')[0]}.0.0.0 Mobile Safari/537.36",
                    new
                    {
                        brands = new[] { new { brand = "Chromium", version = version.Split('.')[0] } },
                        fullVersionList = new[] { new { brand = "Chromium", version } },
                        platform = "Android", platformVersion = "14.0.0", architecture = "",
                        model = "SM-S921U", mobile = true, bitness = ""
                    }),
                new("iPhone · 390 × 844", 390, 844, 3,
                    "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1",
                    null)
            ];
            for (int i = 0; i < devices.Length; i++)
            {
                int index = i;
                devicesMenu.DropDownItems.Add(devices[i].Name, null, async (_, _) => await ApplyDeviceAsync(index, true));
            }
            await ApplyDeviceAsync(0, false);
            if (OpenButton.Enabled)
                Address.Focus();
        }
        catch (Exception ex)
        {
            statusBar.Notify("Browser couldn’t start");
            launcher.SetStatus(ex is WebView2RuntimeNotFoundException
                ? "Microsoft Edge WebView2 Runtime is required."
                : $"Couldn’t start the browser. {ex.Message}", error: true);
        }
    }

    private async Task ApplyDeviceAsync(int index, bool reloadPage)
    {
        if (changingDevice)
            return;
        changingDevice = true;
        OpenButton.Enabled = false;
        var device = devices[index];
        try
        {
            double fit = FitFrame(device.Width, device.Height);

            await EmulateAsync("setDeviceMetricsOverride", new
            {
                width = device.Width, height = device.Height, deviceScaleFactor = device.PixelRatio,
                mobile = true, screenWidth = device.Width, screenHeight = device.Height, scale = fit
            });
            await EmulateAsync("setTouchEmulationEnabled", new { enabled = true, maxTouchPoints = 5 });
            await EmulateAsync("setEmitTouchEventsForMouse", new { enabled = true, configuration = "mobile" });
            await EmulateAsync("setUserAgentOverride",
                device.ClientHints is { } hints
                    ? new { userAgent = device.UserAgent, platform = "Linux armv8l", userAgentMetadata = hints }
                    : (object)new { userAgent = device.UserAgent, platform = "iPhone" });

            selectedDevice = index;
            for (int i = 0; i < devices.Length; i++)
                ((ToolStripMenuItem)devicesMenu.DropDownItems[i]).Checked = i == selectedDevice;
            launcher.SetStatus(DeviceStatus());
            OpenButton.Enabled = true;
            if (reloadPage && CurrentPage is not null)
                Core?.Reload();
        }
        catch (Exception ex)
        {
            statusBar.Notify("Mobile emulation couldn’t start");
            ShowLauncher($"Couldn’t apply mobile emulation. {ex.Message}");
        }
        finally
        {
            changingDevice = false;
        }
    }

    private Task<string> EmulateAsync(string command, object arguments) =>
        Core!.CallDevToolsProtocolMethodAsync($"Emulation.{command}", JsonSerializer.Serialize(arguments));

    private void NavigateFromLauncher()
    {
        if (!OpenButton.Enabled)
            return;
        string input = Address.Text.Trim();
        if (!input.Contains("://", StringComparison.Ordinal))
        {
            bool loopback = Uri.TryCreate("http://" + input, UriKind.Absolute, out var local) && local.IsLoopback;
            input = (loopback ? "http://" : "https://") + input;
        }
        if (!TryWebUri(input, out var uri))
        {
            launcher.SetStatus("Enter a valid HTTP or HTTPS URL.", error: true);
            return;
        }
        OpenWebsite(uri);
    }

    private void OpenWebsite(Uri uri)
    {
        if (!OpenButton.Enabled)
            return;
        if (recentSites is not null)
        {
            recentSites.Record(uri);
            launcher.SetRecentSites(recentSites.Entries);
        }
        initialNavigation = true;
        ShowBrowser();
        Core!.Navigate(uri.AbsoluteUri);
    }

    internal static bool TryWebUri(string? input, out Uri uri)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var result) &&
            result.Scheme is "http" or "https" && result.Host.Length > 0)
        {
            uri = result;
            return true;
        }
        uri = null!;
        return false;
    }

    private void ShowLauncher(string? error = null)
    {
        if (!launcher.Visible && CurrentPage is { } current && error is null)
            Address.Text = current.AbsoluteUri;
        if (controller is not null)
            controller.IsVisible = false;
        browser.Hide();
        launcher.Show();
        UpdateFrameBack();
        if (error is not null)
            launcher.SetStatus(error, error: true);
        else if (OpenButton.Enabled)
            launcher.SetStatus(DeviceStatus());
        Address.Focus();
        Address.SelectAll();
    }

    private string DeviceStatus() => devices[selectedDevice].Name;

    private void ShowBrowser()
    {
        launcher.Hide();
        browser.Show();
        UpdateFrameBack();
        controller!.IsVisible = true;
        controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
    }

    private void UpdateFrameBack() =>
        frameBack.Enabled = launcher.Visible ? CurrentPage is not null : Core?.CanGoBack == true;

    private Action? Shortcut(Keys key) => key switch
    {
        Keys.Control | Keys.Shift | Keys.M => () => menu.Show(this, new Point(8, 8)),
        Keys.Control | Keys.L => () => ShowLauncher(),
        Keys.Escape when launcher.Visible && CurrentPage is not null => ShowBrowser,
        Keys.Alt | Keys.Left when Core?.CanGoBack == true => Core.GoBack,
        Keys.Alt | Keys.Right when Core?.CanGoForward == true => Core.GoForward,
        Keys.Control | Keys.R when CurrentPage is not null => () => Core?.Reload(),
        _ => null
    };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Shortcut(keyData) is not { } action)
            return base.ProcessCmdKey(ref msg, keyData);
        action();
        return true;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        controller?.Close();
        controller = null;
        menu.Dispose();
        base.OnFormClosed(e);
    }

    private void MoveWindow(bool fromMouse)
    {
        ReleaseCapture();
        SendMessage(Handle, 0x0112, fromMouse ? 0xF012 : 0xF010, 0);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
