using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TrayAuth.Core;

namespace TrayAuth.Linux.UI;

/// <summary>
/// One account in the panel: title line, big code, countdown ring. Drawn directly, like the
/// Windows AccountRow, so the two apps read as siblings.
/// </summary>
public sealed class AccountRowControl : Control
{
    private const double ArcRadius = 13;
    private const double ArcThickness = 3;

    private readonly Account _account;
    private readonly Action<AccountRowControl> _copyRequested;

    private string _code = "------";
    private double _fraction = 1d;
    private int _secondsRemaining;
    private bool _hovered;
    private bool _error;
    private string _errorText = string.Empty;
    private DateTime _copiedUntil = DateTime.MinValue;

    public AccountRowControl(Account account, Action<AccountRowControl> copyRequested)
    {
        _account = account;
        _copyRequested = copyRequested;
        Height = LinuxTheme.RowHeight;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public Account Account => _account;

    public string RawCode => _code.Replace(" ", string.Empty);

    public bool HasValidCode => !_error;

    public void RefreshCode(DateTimeOffset now)
    {
        try
        {
            TotpCode code = _account.Generate(now);
            _code = code.Grouped;
            _fraction = code.Fraction;
            _secondsRemaining = code.SecondsRemaining;
            _error = false;
        }
        catch (Exception ex)
        {
            _error = true;
            _errorText = ex.Message;
        }

        if (_copiedUntil != DateTime.MinValue && DateTime.UtcNow > _copiedUntil)
        {
            _copiedUntil = DateTime.MinValue;
        }

        InvalidateVisual();
    }

    public void FlashCopied()
    {
        _copiedUntil = DateTime.UtcNow.AddSeconds(1.4);
        InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        _hovered = true;
        InvalidateVisual();
        base.OnPointerEntered(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _hovered = false;
        InvalidateVisual();
        base.OnPointerExited(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!_error && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _copyRequested(this);
        }

        base.OnPointerPressed(e);
    }

    public override void Render(DrawingContext context)
    {
        var body = new Rect(6, 3, Bounds.Width - 12, Bounds.Height - 6);

        context.DrawRectangle(
            _hovered ? LinuxTheme.SurfaceHoverBrush : LinuxTheme.SurfaceBrush,
            null,
            new RoundedRect(body, 8));

        double textLeft = body.Left + 12;
        double arcCenterX = body.Right - 26;

        DrawTitle(context, textLeft, arcCenterX - textLeft - 12);

        if (_error)
        {
            DrawText(
                context,
                _errorText.Length > 0 ? _errorText : "This account's key is not valid.",
                textLeft,
                32,
                12.5,
                LinuxTheme.DangerBrush);
            return;
        }

        DrawText(context, _code, textLeft - 1, 24, 24, LinuxTheme.TextBrush, bold: true, mono: true);

        if (DateTime.UtcNow < _copiedUntil)
        {
            DrawCopiedPill(context, body);
        }
        else
        {
            DrawCountdown(context, arcCenterX, body.Top + (body.Height / 2));
        }
    }

    private void DrawTitle(DrawingContext context, double left, double available)
    {
        string title = _account.DisplayTitle;
        string subtitle = _account.DisplaySubtitle;

        var titleText = Format(title, 11.5, LinuxTheme.TextBrush);
        titleText.MaxTextWidth = Math.Max(20, available);
        context.DrawText(titleText, new Point(left, 9));

        if (subtitle.Length > 0)
        {
            double subtitleLeft = left + Math.Min(titleText.Width, available) + 6;
            double subtitleWidth = left + available - subtitleLeft;

            if (subtitleWidth > 24)
            {
                var subtitleText = Format("· " + subtitle, 11.5, LinuxTheme.TextSecondaryBrush);
                subtitleText.MaxTextWidth = subtitleWidth;
                context.DrawText(subtitleText, new Point(subtitleLeft, 9));
            }
        }
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        double x,
        double y,
        double size,
        IBrush brush,
        bool bold = false,
        bool mono = false)
    {
        context.DrawText(Format(text, size, brush, bold, mono), new Point(x, y));
    }

    private static FormattedText Format(string text, double size, IBrush brush, bool bold = false, bool mono = false)
    {
        var typeface = new Typeface(
            mono ? new FontFamily("monospace") : FontFamily.Default,
            FontStyle.Normal,
            bold ? FontWeight.Bold : FontWeight.Normal);

        return new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush)
        {
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = 1,
        };
    }

    private void DrawCountdown(DrawingContext context, double centerX, double centerY)
    {
        var center = new Point(centerX, centerY);

        var trackPen = new Pen(LinuxTheme.BorderBrush, ArcThickness);
        context.DrawEllipse(null, trackPen, center, ArcRadius, ArcRadius);

        IBrush color = _secondsRemaining <= 5 ? LinuxTheme.WarningBrush : LinuxTheme.AccentBrush;
        double sweepDegrees = 360d * Math.Clamp(_fraction, 0d, 1d);

        if (sweepDegrees > 0.5)
        {
            var pen = new Pen(color, ArcThickness) { LineCap = PenLineCap.Round };
            context.DrawGeometry(null, pen, BuildArc(center, ArcRadius, sweepDegrees));
        }

        var seconds = Format(
            _secondsRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture),
            10.5,
            _secondsRemaining <= 5 ? LinuxTheme.WarningBrush : LinuxTheme.TextSecondaryBrush);

        context.DrawText(seconds, new Point(centerX - (seconds.Width / 2), centerY - (seconds.Height / 2)));
    }

    /// <summary>Arc from 12 o'clock, sweeping counter-clockwise as the window depletes.</summary>
    private static StreamGeometry BuildArc(Point center, double radius, double sweepDegrees)
    {
        double startAngle = -90d * Math.PI / 180d;
        double endAngle = startAngle - (sweepDegrees * Math.PI / 180d);

        var start = new Point(center.X + (radius * Math.Cos(startAngle)), center.Y + (radius * Math.Sin(startAngle)));
        var end = new Point(center.X + (radius * Math.Cos(endAngle)), center.Y + (radius * Math.Sin(endAngle)));

        var geometry = new StreamGeometry();
        using StreamGeometryContext gc = geometry.Open();
        gc.BeginFigure(start, isFilled: false);
        gc.ArcTo(
            end,
            new Size(radius, radius),
            rotationAngle: 0,
            isLargeArc: sweepDegrees > 180,
            SweepDirection.CounterClockwise);
        gc.EndFigure(false);

        return geometry;
    }

    private void DrawCopiedPill(DrawingContext context, Rect body)
    {
        const double pillWidth = 62;
        const double pillHeight = 22;

        var pill = new Rect(
            body.Right - pillWidth - 10,
            body.Top + ((body.Height - pillHeight) / 2),
            pillWidth,
            pillHeight);

        context.DrawRectangle(LinuxTheme.AccentDimBrush, null, new RoundedRect(pill, pillHeight / 2));

        var label = Format("Copied", 11, LinuxTheme.AccentBrush);
        context.DrawText(
            label,
            new Point(pill.Center.X - (label.Width / 2), pill.Center.Y - (label.Height / 2)));
    }
}
