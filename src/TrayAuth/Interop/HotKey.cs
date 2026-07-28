namespace TrayAuth.Interop;

/// <summary>
/// A single system-wide hotkey bound to an existing window handle.
///
/// Registration is allowed to fail: if another application already owns the combination, the tray
/// icon still works and there is nothing worth interrupting the user about.
/// </summary>
public sealed class HotKey : IDisposable
{
    public const int HotKeyId = 0xA711;

    private IntPtr _handle;
    private bool _registered;

    public bool IsRegistered => _registered;

    public Keys Key { get; private set; }

    public HotKeyModifiers Modifiers { get; private set; }

    public bool Register(IntPtr windowHandle, HotKeyModifiers modifiers, Keys key)
    {
        Unregister();

        _handle = windowHandle;
        Modifiers = modifiers;
        Key = key;

        _registered = NativeMethods.RegisterHotKey(
            windowHandle,
            HotKeyId,
            (uint)(modifiers | HotKeyModifiers.NoRepeat),
            (uint)key);

        return _registered;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_handle, HotKeyId);
        _registered = false;
    }

    public string Describe()
    {
        var parts = new List<string>();

        if (Modifiers.HasFlag(HotKeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    public void Dispose() => Unregister();
}
