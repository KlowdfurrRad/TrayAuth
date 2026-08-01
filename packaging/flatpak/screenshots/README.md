# Store-listing screenshots

`panel.png` is referenced by the AppStream metainfo and shown on the Flathub page.

## Never screenshot a real vault

A 2FA app's account list is a public statement of which services you protect, and any email
address in it belongs to a person who did not agree to appear in an app store. Flathub mirrors
worldwide and both the image and this repository's history are permanent.

Use `demo-accounts.json` instead. All three entries share the RFC 6238 example secret, so the
codes on screen are genuine but protect nothing.

## Producing the screenshot

1. Back up first, so the demo import is trivially reversible:
   tray menu -> **Export all accounts...**
2. Panel -> **Import file** -> pick `demo-accounts.json` -> import.
3. Delete your real accounts for the moment (right-click each row -> Delete), so only the three
   demo entries remain.
4. Open the panel and capture **just that window** - on GNOME, `Alt` + `PrtSc` captures the
   focused window rather than the whole screen.
5. Save it here as `panel.png`.
6. Delete the demo accounts, then re-import your export from step 1.

Sanity check before committing: open the PNG and read every line of text in it. If any of it is
real, retake it.
