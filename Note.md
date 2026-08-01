# Note: language and toolkit choices for the Linux app

The Linux app is C# / .NET 8 + Avalonia — the same language and core as the Windows app.
That is not the most *native* stack for a GNOME app. This note records what would be, and
why we chose otherwise.

## The more native options

| Stack | Native feel | Keeps our tested core? | Catch |
|---|---|---|---|
| Rust + GTK4/libadwaita | Best - the modern GNOME standard | No - full rewrite | Two codebases forever |
| Python + GTK4 | Very good, fastest to write | No - full rewrite | Rewrites and re-tests the crypto |
| C# + GTK4 (GirCore bindings) | Very good | **Yes** | Young bindings; GTK4 has no tray API, so StatusNotifier gets hand-rolled |
| C# + Avalonia (chosen) | Adequate - Fluent look, not Adwaita | **Yes** | ~60 MB binary, guest aesthetics |

"StatusNotifier hand-rolled" means: on Linux the shell draws tray icons, and apps describe
theirs over DBus using the StatusNotifierItem + DBusMenu protocols. GTK4 removed its tray API,
so a GTK4 app must implement those protocols itself (~hundreds of lines). Avalonia ships that
plumbing built in - and this app's primary surface *is* the tray menu.

## Why Avalonia won anyway

The tested logic is the product. TOTP generation verified against the RFC 6238 vectors, the
Google Authenticator migration parser, vault and export formats byte-compatible with the
Windows app - all of it lives in `src/TrayAuth.Core` and carries over unchanged, with its
103 portable tests. A wrong pixel is cosmetic; a wrong code locks someone out of an account.
Rewriting the core in another language to gain Adwaita styling is a bad trade for a
one-maintainer project.

## If this is ever revisited

The core extraction keeps the door open: swap the UI layer for GTK4 (most likely via GirCore,
staying in C#) without touching a line of `TrayAuth.Core`. Do it when the Linux app has real
users who care about native look, or when GNOME's Wayland restrictions (no window
self-positioning, portal-only global shortcuts) are better served by a toolkit closer to the
platform. Not before.
