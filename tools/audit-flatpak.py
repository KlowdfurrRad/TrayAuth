"""Pre-flight audit of the Flatpak submission against known flatpak-builder-lint rules."""
import json
import re
import sys
import urllib.request
import xml.etree.ElementTree as ET

import yaml

MANIFEST = "packaging/flatpak/flathub/io.github.KlowdfurrRad.TrayAuth.yml"
DEV_MANIFEST = "packaging/flatpak/io.github.KlowdfurrRad.TrayAuth.yml"
METAINFO = "packaging/flatpak/io.github.KlowdfurrRad.TrayAuth.metainfo.xml"
DESKTOP = "packaging/flatpak/io.github.KlowdfurrRad.TrayAuth.desktop"
APPID = "io.github.KlowdfurrRad.TrayAuth"

problems, warnings = [], []


def check(label, ok, detail="", warn_only=False):
    if ok:
        print(f"  [ok  ] {label}{(' - ' + detail) if detail else ''}")
    else:
        print(f"  [{'WARN' if warn_only else 'FAIL'}] {label}{(' - ' + detail) if detail else ''}")
        (warnings if warn_only else problems).append(label)


print("=== manifest ===")
m = yaml.safe_load(open(MANIFEST, encoding="utf-8"))

check("app-id matches the filename", MANIFEST.endswith(f"{APPID}.yml"))
check("app-id has at least 4 components", len(APPID.split(".")) >= 4, APPID)
check("uses a supported runtime", m["runtime"] == "org.freedesktop.Platform", m["runtime"])
check("command is set", bool(m.get("command")), m.get("command"))
check("no 'cleanup' of the whole prefix", "/" not in (m.get("cleanup") or []))

fa = m["finish-args"]
print(f"  finish-args: {fa}")
check("no --filesystem=host or =home", not any(a.startswith("--filesystem=host") or a.startswith("--filesystem=home") for a in fa))
check("no --share=network at runtime", "--share=network" not in fa,
      "app makes no network calls")
check("no --socket=session-bus (too broad)", "--socket=session-bus" not in fa)
check("no --socket=system-bus", "--socket=system-bus" not in fa)
check("no --device=all", "--device=all" not in fa)
check("x11 accompanied by --share=ipc", ("--socket=x11" not in fa) or ("--share=ipc" in fa))
check("does not mix x11 and fallback-x11",
      not ("--socket=x11" in fa and "--socket=fallback-x11" in fa))

# Sources must be pinned to something immutable.
app_module = m["modules"][-1]
git_src = app_module["sources"][0]
check("app source is a git commit, not a branch", bool(git_src.get("commit")), git_src.get("commit", "")[:12])
check("app source has a tag as well", bool(git_src.get("tag")), git_src.get("tag"))

for mod in m["modules"][:-1]:
    for s in mod["sources"]:
        if s.get("type") == "git":
            check(f"{mod['name']} git source pinned to a commit", bool(s.get("commit")))
        if s.get("type") == "archive":
            check(f"{mod['name']} archive has a checksum", bool(s.get("sha256") or s.get("sha512")))

# Dev and submission manifests must not drift apart.
d = yaml.safe_load(open(DEV_MANIFEST, encoding="utf-8"))
check("dev and flathub manifests agree on finish-args", d["finish-args"] == m["finish-args"])
check("dev and flathub manifests agree on modules",
      [x["name"] for x in d["modules"]] == [x["name"] for x in m["modules"]])
check("dev and flathub manifests agree on build-commands",
      d["modules"][-1]["build-commands"] == m["modules"][-1]["build-commands"])

print("\n=== metainfo ===")
root = ET.parse(METAINFO).getroot()


def text(tag):
    el = root.find(tag)
    return el.text.strip() if el is not None and el.text else None


check("component type is desktop-application", root.get("type") == "desktop-application")
check("id matches the app-id", text("id") == APPID, text("id"))
check("name present", bool(text("name")))
check("summary present", bool(text("summary")))
check("summary does not end with a period", not (text("summary") or "").endswith("."))
check("summary is short enough", len(text("summary") or "") <= 90, f"{len(text('summary') or '')} chars")
check("metadata_license present", bool(text("metadata_license")))
check("project_license present", bool(text("project_license")))
check("description present", root.find("description") is not None)
check("launchable present", root.find("launchable") is not None)
check("launchable matches the desktop file",
      (root.find("launchable").text or "").strip() == f"{APPID}.desktop")
check("content_rating present", root.find("content_rating") is not None)
check("developer name present", root.find("developer/name") is not None)
check("homepage url present", any(u.get("type") == "homepage" for u in root.findall("url")))
check("bugtracker url present", any(u.get("type") == "bugtracker" for u in root.findall("url")))
check("at least one release", root.find("releases/release") is not None)

shots = root.findall("screenshots/screenshot")
check("at least one screenshot", len(shots) >= 1, f"{len(shots)}")
check("exactly one default screenshot",
      sum(1 for s in shots if s.get("type") == "default") == 1)
for s in shots:
    cap = s.find("caption")
    check("caption present and unpunctuated",
          cap is not None and cap.text and not cap.text.strip().endswith((".", "!", "?")),
          (cap.text if cap is not None else ""))

print("\n=== screenshot URLs ===")
print("  (reachability is checked separately - Python does not trust this machine's")
print("   TLS-intercepting root certificate, so a failure here would be meaningless)")
for s in shots:
    url = s.find("image").text.strip()
    check("URL is raw.githubusercontent over https",
          url.startswith("https://raw.githubusercontent.com/KlowdfurrRad/TrayAuth/"),
          url.rsplit("/", 1)[-1])

print("\n=== desktop file ===")
desktop = open(DESKTOP, encoding="utf-8").read()
check("Exec matches the manifest command",
      re.search(r"^Exec=(.+)$", desktop, re.M).group(1).split()[0] == m["command"])
check("Icon equals the app-id", re.search(r"^Icon=(.+)$", desktop, re.M).group(1).strip() == APPID)
check("Type=Application", "Type=Application" in desktop)
check("has Categories", "Categories=" in desktop)

print("\n=== offline sources ===")
srcs = json.load(open("packaging/flatpak/nuget-sources.json", encoding="utf-8"))
check("all entries have sha512", all(len(s.get("sha512", "")) == 128 for s in srcs), f"{len(srcs)} packages")
check("all entries use https", all(s["url"].startswith("https://") for s in srcs))
# Every runtime/host pack must carry the same version. Match on the version rather than the
# package name: "aspnetcore.app.runtime" contains "netcore.app.runtime" as a substring, which
# made an earlier version of this check report a false failure.
pack_versions = {
    re.search(r"\.(\d+\.\d+\.\d+)\.nupkg$", s["dest-filename"]).group(1)
    for s in srcs
    if re.search(r"app\.(runtime|host)\.linux-x64", s["dest-filename"])
}
check("all runtime/host packs share one version", len(pack_versions) == 1, ", ".join(pack_versions))

print()
if problems:
    print(f"FAILED: {problems}")
    sys.exit(1)
if warnings:
    print(f"Passed with warnings: {warnings}")
print("PRE-FLIGHT AUDIT PASSED")
