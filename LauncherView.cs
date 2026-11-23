using System.Drawing.Drawing2D;

namespace PocketViewport;

internal sealed class LauncherView : Panel
{
    private static readonly Color Surface = Color.FromArgb(20, 24, 33);
    private static readonly Color InputSurface = Color.FromArgb(27, 33, 46);
    private static readonly Color TextColor = Color.FromArgb(237, 241, 248);
    private static readonly Color MutedText = Color.FromArgb(162, 174, 194);
    private static readonly Color Accent = Color.FromArgb(166, 193, 247);
    private static readonly Color ErrorText = Color.FromArgb(255, 166, 158);
    private readonly Font titleFont = new("Segoe UI", 22, FontStyle.Bold);
    private readonly Font labelFont = new("Segoe UI", 10);
    private readonly Font inputFont = new("Segoe UI", 11);
    private readonly ViewportIcon icon = new();
    private readonly Label title = new()
    {
        Text = "Pocket Viewport", ForeColor = TextColor, TextAlign = ContentAlignment.MiddleCenter
    };
    private readonly Label urlLabel = new()
    {
        Text = "Website URL", ForeColor = TextColor, TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label status = new()
    {
        TextAlign = ContentAlignment.TopCenter, UseMnemonic = false, AutoEllipsis = true
    };
    private readonly Panel field = new() { BackColor = Surface };
    private readonly Label recentTitle = new()
    {
        Text = "Recent websites", ForeColor = MutedText, TextAlign = ContentAlignment.MiddleLeft,
        Visible = false
    };
    private readonly Button[] recentButtons = new Button[RecentSites.Capacity];
    private readonly ToolTip recentToolTips = new();
    private int recentCount;
    public event Action<Uri>? RecentSiteSelected;

    public TextBox Address { get; } = new()
    {
        AccessibleName = "Website URL", PlaceholderText = "example.com or localhost:3000",
        BackColor = InputSurface, ForeColor = TextColor, BorderStyle = BorderStyle.None
    };
    public Button OpenButton { get; } = new()
    {
        Text = "Open website", FlatStyle = FlatStyle.Flat, BackColor = Accent,
        ForeColor = Color.FromArgb(15, 30, 56), Enabled = false, Cursor = Cursors.Hand
    };

    public LauncherView()
    {
        BackColor = Surface;
        DoubleBuffered = true;
        AutoScroll = true;
        title.Font = titleFont;
        urlLabel.Font = labelFont;
        status.Font = labelFont;
        recentTitle.Font = labelFont;
        Address.Font = inputFont;
        OpenButton.Font = inputFont;
        OpenButton.FlatAppearance.BorderSize = 0;
        OpenButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(185, 208, 255);
        OpenButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(144, 174, 232);
        field.Paint += PaintField;
        field.Click += (_, _) => Address.Focus();
        Address.Enter += (_, _) => field.Invalidate();
        Address.Leave += (_, _) => field.Invalidate();
        icon.MouseDown += (_, e) => OnMouseDown(e);
        title.MouseDown += (_, e) => OnMouseDown(e);
        field.Controls.Add(Address);
        Controls.AddRange([icon, title, urlLabel, field, OpenButton, status, recentTitle]);
        for (int i = 0; i < recentButtons.Length; i++)
        {
            var button = new Button
            {
                AutoEllipsis = true, UseMnemonic = false, Font = labelFont, TextAlign = ContentAlignment.MiddleLeft,
                BackColor = InputSurface, ForeColor = TextColor, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand, Visible = false, Enabled = OpenButton.Enabled
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(39, 49, 67);
            button.Click += (_, _) =>
            {
                if (button.Tag is Uri uri)
                    RecentSiteSelected?.Invoke(uri);
            };
            recentButtons[i] = button;
            Controls.Add(button);
        }
        OpenButton.EnabledChanged += (_, _) =>
        {
            foreach (var button in recentButtons)
                button.Enabled = OpenButton.Enabled;
        };
        SetStatus("Starting browser…");
        SizeChanged += (_, _) => LayoutContent();
        LayoutContent();
    }

    public void SetStatus(string text, bool error = false)
    {
        status.Text = text;
        status.ForeColor = error ? ErrorText : MutedText;
        LayoutContent();
    }

    public void SetRecentSites(IReadOnlyList<Uri> sites)
    {
        recentCount = Math.Min(sites.Count, recentButtons.Length);
        recentTitle.Visible = recentCount > 0;
        for (int i = 0; i < recentButtons.Length; i++)
        {
            var button = recentButtons[i];
            var uri = i < recentCount ? sites[i] : null;
            button.Tag = uri;
            if (uri is not null)
            {
                button.Text = uri.Authority + (uri.PathAndQuery == "/" ? "" : uri.PathAndQuery) + uri.Fragment;
                button.AccessibleName = $"Open {uri.AbsoluteUri}";
                recentToolTips.SetToolTip(button, uri.AbsoluteUri);
            }
            else
            {
                recentToolTips.SetToolTip(button, null);
            }
            button.Visible = uri is not null;
        }
        LayoutContent();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        LayoutContent();
    }

    private void LayoutContent()
    {
        int width = Math.Max(1, Math.Min(Pixels(304), ClientSize.Width - Pixels(44)));
        int left = (ClientSize.Width - width) / 2;
        int statusHeight = Math.Clamp(status.GetPreferredSize(new Size(width, 0)).Height, Pixels(26), Pixels(65));
        int recentHeading = Pixels(307) + statusHeight + Pixels(12);
        int rowStart = recentHeading + Pixels(29);
        int rowHeight = Pixels(35);
        int groupHeight = recentCount > 0 ? rowStart + recentCount * rowHeight : Pixels(307) + statusHeight;
        AutoScrollMinSize = new Size(0, groupHeight + Pixels(24));
        int top = Math.Max(Pixels(12), (ClientSize.Height - groupHeight) / 2) + AutoScrollPosition.Y;
        int art = Pixels(74);
        icon.SetBounds((ClientSize.Width - art) / 2, top, art, art);
        title.SetBounds(left, top + Pixels(90), width, Pixels(43));
        urlLabel.SetBounds(left, top + Pixels(157), width, Pixels(22));
        field.SetBounds(left, top + Pixels(185), width, Pixels(48));
        Address.SetBounds(Pixels(13), (field.Height - Address.PreferredHeight) / 2,
            Math.Max(1, width - Pixels(26)), Address.PreferredHeight);
        OpenButton.SetBounds(left, top + Pixels(245), width, Pixels(44));
        RoundButton();
        status.SetBounds(left, top + Pixels(307), width, statusHeight);
        recentTitle.SetBounds(left, top + recentHeading, width, Pixels(23));
        for (int i = 0; i < recentCount; i++)
        {
            recentButtons[i].Padding = new Padding(Pixels(9), 0, Pixels(9), 0);
            recentButtons[i].SetBounds(left, top + rowStart + i * rowHeight, width, rowHeight - Pixels(2));
        }
        field.Invalidate();
    }

    private int Pixels(int logical) => LogicalToDeviceUnits(logical);

    private void RoundButton()
    {
        if (OpenButton.Width < 2 || OpenButton.Height < 2)
            return;
        using var path = PhoneWindow.RoundedPath(OpenButton.ClientRectangle, Pixels(9));
        OpenButton.Region = new Region(path);
    }

    private void PaintField(object? sender, PaintEventArgs e)
    {
        if (field.Width < 3 || field.Height < 3)
            return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = Rectangle.Inflate(field.ClientRectangle, -1, -1);
        using var path = PhoneWindow.RoundedPath(bounds, Pixels(9));
        using var background = new SolidBrush(InputSurface);
        using var border = new Pen(Address.Focused ? Accent : Color.FromArgb(62, 75, 96),
            Address.Focused ? Pixels(2) : Pixels(1));
        e.Graphics.FillPath(background, path);
        e.Graphics.DrawPath(border, path);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        titleFont.Dispose();
        labelFont.Dispose();
        inputFont.Dispose();
        recentToolTips.Dispose();
    }

    private sealed class ViewportIcon : Control
    {
        public ViewportIcon()
        {
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.ScaleTransform(Width / 74f, Height / 74f);
            using var tile = PhoneWindow.RoundedPath(new Rectangle(1, 1, 72, 72), 19);
            using var fill = new SolidBrush(Color.FromArgb(30, 41, 62));
            using var edge = new Pen(Color.FromArgb(54, 71, 101));
            e.Graphics.FillPath(fill, tile);
            e.Graphics.DrawPath(edge, tile);

            using var phone = PhoneWindow.RoundedPath(new Rectangle(26, 17, 22, 40), 5);
            using var outline = new Pen(Accent, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var detail = new Pen(Color.FromArgb(113, 139, 190), 1.3f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawPath(outline, phone);
            e.Graphics.DrawLine(outline, 33, 25, 41, 25);
            e.Graphics.DrawLine(outline, 34, 51, 40, 51);
            e.Graphics.DrawLines(detail, new[] { new Point(21, 29), new Point(16, 29), new Point(16, 37) });
            e.Graphics.DrawLines(detail, new[] { new Point(53, 45), new Point(58, 45), new Point(58, 37) });
        }
    }
}
