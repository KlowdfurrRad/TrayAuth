namespace TrayAuth.Core;

/// <summary>
/// Copies a code and takes it back out of the clipboard a short while later.
///
/// The clearing step checks that the clipboard still holds the code it put there. Without that,
/// anything you copied in the meantime would get wiped by our timer — a small detail that decides
/// whether the auto-clear feels safe or hostile.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new();
    private string? _pending;

    public ClipboardService()
    {
        _timer.Tick += OnTick;
    }

    /// <summary>Seconds the code is left on the clipboard before it is cleared.</summary>
    public int ClearAfterSeconds { get; set; } = 20;

    /// <summary>Raised when the clipboard is cleared, or when the pending clear is abandoned.</summary>
    public event EventHandler? Cleared;

    public bool TryCopy(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }

        if (!TrySetClipboard(code))
        {
            return false;
        }

        _pending = code;

        _timer.Stop();
        _timer.Interval = Math.Max(1, ClearAfterSeconds) * 1000;
        _timer.Start();

        return true;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();

        string? pending = _pending;
        _pending = null;

        if (pending is null)
        {
            return;
        }

        try
        {
            if (Clipboard.ContainsText() && string.Equals(Clipboard.GetText(), pending, StringComparison.Ordinal))
            {
                Clipboard.Clear();
            }
        }
        catch
        {
            // Another process had the clipboard open. Leaving the code in place is the only option.
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The clipboard is a shared, singly-owned resource: a busy process can hold it just long enough
    /// for one attempt to fail, so a couple of quick retries turn a visible error into a non-event.
    /// </summary>
    private static bool TrySetClipboard(string text)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }

    public void Dispose()
    {
        _timer.Tick -= OnTick;
        _timer.Dispose();
    }
}
