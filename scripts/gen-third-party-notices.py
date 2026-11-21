#!/usr/bin/env python3
"""Generates THIRD_PARTY_NOTICES.md from the locked Flutter packages and the NuGet packages
the Windows app uses. Reads license files from the local pub and NuGet caches, so run
`flutter pub get` and `dotnet restore` first.

Usage: python scripts/gen-third-party-notices.py
"""
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PUB = os.environ.get("PUB_CACHE", os.path.join(os.environ.get("LOCALAPPDATA", ""), "Pub", "Cache")) + "/hosted/pub.dev"
NUGET = os.path.join(os.path.expanduser("~"), ".nuget", "packages")

PATTERNS = [
    ("MPL-2.0", r"Mozilla Public License"),
    ("Apache-2.0", r"Apache License"),
    ("MIT", r"MIT License|Permission is hereby granted, free of charge"),
    ("BSD", r"Redistribution and use in source and binary forms"),
]


def classify(text):
    head = text[:8000]
    for name, pat in PATTERNS:
        if re.search(pat, head, re.I):
            return name
    if re.search(r"GNU (Lesser |Affero )?General Public License", head[:300], re.I):
        return "GPL-family (CHECK)"
    return "UNKNOWN (CHECK)"


def copyright_line(text):
    for line in text.splitlines():
        if re.search(r"copyright|\(c\)|©", line, re.I) and len(line.strip()) < 200:
            return line.strip().lstrip("*# ")
    return ""


def flutter_packages():
    lock = open(os.path.join(ROOT, "app", "pubspec.lock"), encoding="utf-8").read()
    out = []
    for m in re.finditer(r"^  ([a-z_0-9]+):\n((?:    .*\n|\n)+?)(?=^  [a-z_0-9]+:\n|\Z)", lock, re.M):
        name, body = m.groups()
        src = re.search(r"source: (\w+)", body)
        ver = re.search(r'version: "([^"]+)"', body)
        if not (src and ver) or src.group(1) == "sdk":
            continue
        files = glob.glob(f"{PUB}/{name}-{ver.group(1)}/LICEN*") + glob.glob(f"{PUB}/{name}-{ver.group(1)}/COPYING*")
        text = open(files[0], encoding="utf-8", errors="ignore").read() if files else ""
        out.append((name, ver.group(1), classify(text) if text else "UNKNOWN (CHECK)", copyright_line(text)))
    return sorted(out)


def nuget_packages():
    # Direct references plus the transitive packages that end up next to the executable.
    wanted = ["librehardwaremonitorlib", "diskinfotoolkit", "ramspdtoolkit-ndd", "blacksharp.core", "hidsharp",
              "mono.posix.netstandard", "localstorage", "microsoft.web.webview2", "mqttnet", "naudio", "nlog",
              "nlog.extensions.logging", "qrcoder", "swashbuckle.aspnetcore", "velopack", "newtonsoft.json",
              "system.management", "system.diagnostics.performancecounter"]
    out = []
    for pid in wanted:
        dirs = sorted(glob.glob(os.path.join(NUGET, pid, "*")))
        if not dirs:
            out.append((pid, "?", "NOT RESTORED (CHECK)", ""))
            continue
        d = dirs[-1]
        ver = os.path.basename(d)
        nuspecs = glob.glob(os.path.join(d, "*.nuspec"))
        lic, cr, title = "", "", pid
        if nuspecs:
            root = ET.parse(nuspecs[0]).getroot()
            ns = {"n": root.tag.split("}")[0].strip("{")}
            meta = root.find("n:metadata", ns)
            node = meta.find("n:license", ns)
            if node is not None:
                lic = node.text if node.get("type") == "expression" else "see license file"
                if node.get("type") == "file":
                    f = os.path.join(d, node.text)
                    if os.path.exists(f):
                        lic = classify(open(f, encoding="utf-8", errors="ignore").read())
            else:
                url = meta.find("n:licenseUrl", ns)
                lic = "not declared in package (CHECK)" + (f" ({url.text})" if url is not None else "")
            c = meta.find("n:copyright", ns)
            cr = c.text.strip() if c is not None and c.text else ""
            t = meta.find("n:id", ns)
            title = t.text if t is not None else pid
        out.append((title, ver, lic, cr))
    return out


# Packages whose metadata declares no license; verified by hand against the project's own repository.
OVERRIDES = {
    "LocalStorage": "MIT (GitHub license record of hanssens/localstorage-for-dotnet; not declared in the package)",
    "Mono.Posix.NETStandard": "MIT (Mono class libraries; the package's license link points to the Mono project license)",
}


def table(rows):
    lines = ["| Package | Version | License | Copyright |", "|---|---|---|---|"]
    for n, v, l, c in rows:
        lines.append(f"| {n} | {v} | {l} | {c.replace('|', '/')} |")
    return "\n".join(lines)


def main():
    flutter = flutter_packages()
    nuget = [(n, v, OVERRIDES.get(n, l), c) for n, v, l, c in nuget_packages()]
    bad = [r for r in flutter + nuget if "CHECK" in r[2]]
    apache = open(os.path.join(ROOT, "LICENSE"), encoding="utf-8").read().rstrip()
    text = f"""# Third-party notices

KZ Companion includes or depends on the open-source components below. This file is generated by
`scripts/gen-third-party-notices.py` from the locked dependency versions. It ships with the Windows installer
and is attached to each GitHub Release. The license of KZ Companion itself, and the unresolved question about
the Android app's license, are described in [NOTICE](NOTICE) and [docs/license-audit.md](docs/license-audit.md).

License texts: MIT <https://opensource.org/license/mit>, BSD <https://opensource.org/license/bsd-3-clause>,
Apache-2.0 <https://www.apache.org/licenses/LICENSE-2.0>, MPL-2.0 <https://www.mozilla.org/MPL/2.0/>.
Each package's own license file, with its exact wording, is in the package linked from its page on
<https://pub.dev> (Flutter) or <https://www.nuget.org> (.NET).

## Windows app (.NET and NuGet)

{table(nuget)}

The MPL-2.0 packages (LibreHardwareMonitorLib, DiskInfoToolkit, RAMSPDToolkit-NDD, BlackSharp.Core) are used
**unmodified**; their source is available from the upstream projects:
<https://github.com/LibreHardwareMonitor/LibreHardwareMonitor>. The self-contained Windows build also includes the
.NET runtime (MIT, Microsoft; see <https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT>).

The optional CPU-sensor driver PawnIO and the `adb` tool are **not** bundled; they are installed or downloaded on
the user's request and are covered by their own licenses.

## Android app and web configurator (Flutter and pub.dev)

The Flutter framework and engine are BSD-3-Clause, Copyright The Flutter Authors.
Google ML Kit barcode scanning (`com.google.mlkit:barcode-scanning`, bundled in the Android APK by `mobile_scanner`)
is **proprietary software under Google's terms**, not open source (<https://developers.google.com/ml-kit/terms>).
Its use alongside the Android app's license is an open question recorded in `docs/license-audit.md`.
AndroidX and CameraX libraries are Apache-2.0.

{table(flutter)}

## Apache License 2.0 (full text)

Applies to the Apache-2.0 components above and to the upstream DroidDeck code as declared by its author.

```
{apache}
```
"""
    with open(os.path.join(ROOT, "THIRD_PARTY_NOTICES.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    if bad:
        print("NEEDS MANUAL CHECK:", file=sys.stderr)
        for r in bad:
            print("  ", r[0], r[1], r[2], file=sys.stderr)
    print(f"wrote THIRD_PARTY_NOTICES.md: {len(nuget)} NuGet, {len(flutter)} Flutter packages")


if __name__ == "__main__":
    main()
