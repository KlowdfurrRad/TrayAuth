using System.Drawing.Drawing2D;
using TrayAuth.Core;

namespace TrayAuth.UI;

/// <summary>
/// One account in the list: title, code, and a countdown ring. Drawn by hand rather than assembled
/// from labels, because the ring needs to animate and the whole row needs to act as one button.
/// </summary>
public sealed class AccountRow : UserControl
{
    private const int ArcRadius = 13;
    private const int ArcThickness = 3;

    private readonly Account _account;

    private string _code = "------";
    private double _fraction = 1d;
    private int _secondsRemaining;
    private bool _hovered;
    private bool _error;
    private string _errorText = string.Empty;
    private DateTime _copiedUntil = DateTime.MinValue;

    public AccountRow(Account account)
    {
        _account = account;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint,
            true);

        Height = Theme.RowHeight;
        Width = Theme.PanelWidth;
        Margin = Padding.Empty;
        BackColor = Theme.Background;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public Account Account => _account;

    public event EventHandler? CopyRequested;

    public event EventHandler? EditRequested;

    public event EventHandler? DeleteRequested;

    public event EventHandler? ExportRequested;

    /// <summary>Recomputes this row's code. Failures are shown in place, never thrown at the panel.</summary>
    public void Refresh(DateTimeOffset now)
    {
        try
        {
            TotpCode code = _account.Generate(now);

            string next = code.Grouped;
            bool changed = next != _code
                || Math.Abs(code.Fraction - _fraction) > 0.001
                || _error;

            _code = next;
            _fraction = code.Fraction;
            _secondsRemaining = code.SecondsRemaining;
            _error = false;

            if (changed)
            {
                Invalidate();
            }
        }
        catch (Exception ex)
        {
            if (!_error)
            {
                _error = true;
                _errorText = ex.Message;
                Invalidate();
            }
        }
    }

    /// <summary>The code without grouping spaces — what actually goes on the clipboard.</summary>
    public string RawCode => _code.Replace(" ", string.Empty);

    public bool HasValidCode => !_error;

    public void FlashCopied()
    {
        _copiedUntil = DateTime.UtcNow.AddSeconds(1.4);
        Invalidate();
    }

    public bool CopiedFlashExpired => _copiedUntil != DateTime.MinValue && DateTime.UtcNow > _copiedUntil;

    public void ClearCopiedFlash()
    {
        _copiedUntil = DateTime.MinValue;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !_error)
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Button == MouseButtons.Right)
        {
            ShowRowMenu(e.Location);
        }

        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space && !_error)
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void ShowRowMenu(Point location)
    {
        var menu = new ContextMenuStrip
        {
            RenderMode = ToolStripRenderMode.System,
        };

        menu.Items.Add("Copy code", null, (_, _) => CopyRequested?.Invoke(this, EventArgs.Empty)).Enabled = !_error;
        menu.Items.Add("Edit…", null, (_, _) => EditRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Export…", null, (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete…", null, (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty));

        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(this, location);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var body = new Rectangle(6, 3, Width - 12, Height - 6);

        using (var path = Theme.RoundedRect(body, 8))
        using (var brush = new SolidBrush(_hovered ? Theme.SurfaceHover : Theme.Surface))
        {
            g.FillPath(brush, path);
        }

        bool showCopied = DateTime.UtcNow < _copiedUntil;
        int textLeft = body.Left + 12;
        int arcCenterX = body.Right - 26;

        DrawTitle(g, textLeft, arcCenterX - textLeft - 12);

        if (_error)
        {
            DrawError(g, body, textLeft);
            return;
        }

        DrawCode(g, textLeft);

        if (showCopied)
        {
            DrawCopiedPill(g, body);
        }
        else
        {
            DrawCountdown(g, arcCenterX, body.Top + (body.Height / 2));
        }
    }

    private void DrawTitle(Graphics g, int left, int available)
    {
        string subtitle = _account.DisplaySubtitle;
        string title = _account.DisplayTitle;

        using var titleBrush = new SolidBrush(Theme.Text);
        using var subtitleBrush = new SolidBrush(Theme.TextSecondary);

        var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Trimming = StringTrimming.EllipsisCharacter,
        };

        var titleBounds = new RectangleF(left, 9, available, 16);
        g.DrawString(title, Theme.Small, titleBrush, titleBounds, format);

        if (subtitle.Length > 0)
        {
            SizeF titleSize = g.MeasureString(title, Theme.Small, available, format);
            float subtitleLeft = left + titleSize.Width + 4;
            float subtitleWidth = (left + available) - subtitleLeft;

            if (subtitleWidth > 24)
            {
                g.DrawString("· " + subtitle, Theme.Small, subtitleBrush,
                    new RectangleF(subtitleLeft, 9, subtitleWidth, 16), format);
            }
        }
    }

    private void DrawCode(Graphics g, int left)
    {
        using var brush = new SolidBrush(Theme.Text);
        g.DrawString(_code, Theme.Code, brush, new PointF(left - 2, 25));
    }

    private void DrawError(Graphics g, Rectangle body, int left)
    {
        using var brush = new SolidBrush(Theme.Danger);
        var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Trimming = StringTrimming.EllipsisCharacter,
        };

        g.DrawString(
            _errorText.Length > 0 ? _errorText : "This account's key is not valid.",
            Theme.Body,
            brush,
            new RectangleF(left, 30, body.Width - 24, 18),
            format);
    }

    private void DrawCountdown(Graphics g, int centerX, int centerY)
    {
        var arcBounds = new Rectangle(centerX - ArcRadius, centerY - ArcRadius, ArcRadius * 2, ArcRadius * 2);

        using (var track = new Pen(Theme.Border, ArcThickness))
        {
            g.DrawEllipse(track, arcBounds);
        }

        // Amber in the last few seconds, so a code that is about to roll over looks like one.
        Color color = _secondsRemaining <= 5 ? Theme.Warning : Theme.Accent;
        float sweep = (float)(360d * Math.Clamp(_fraction, 0d, 1d));

        if (sweep > 0.5f)
        {
            using var pen = new Pen(color, ArcThickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };

            g.DrawArc(pen, arcBounds, -90f, -sweep);
        }

        using var textBrush = new SolidBrush(_secondsRemaining <= 5 ? Theme.Warning : Theme.TextSecondary);
        var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        g.DrawString(
            _secondsRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Theme.Small,
            textBrush,
            arcBounds,
            format);
    }

    private void DrawCopiedPill(Graphics g, Rectangle body)
    {
        const int pillWidth = 62;
        const int pillHeight = 22;

        var pill = new Rectangle(body.Right - pillWidth - 10, body.Top + ((body.Height - pillHeight) / 2), pillWidth, pillHeight);

        using (var path = Theme.RoundedRect(pill, pillHeight / 2))
        using (var brush = new SolidBrush(Theme.AccentDim))
        {
            g.FillPath(brush, path);
        }

        using var textBrush = new SolidBrush(Theme.Accent);
        var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        g.DrawString("Copied", Theme.Small, textBrush, pill, format);
    }
}
