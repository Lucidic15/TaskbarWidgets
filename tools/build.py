"""Builds the standalone apps: each Windhawk mod in mods/ with the host in standalone/, into
build/<App>.exe (64-bit). Needs Python 3 and an LLVM MinGW Clang: Windhawk's own (found where
Windhawk installs it), or one given with --compiler (a folder with clang++ in it).

Usage: python tools/build.py [--compiler DIR] [mod ids...]
"""
import argparse
import os
import re
import shutil
import struct
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Each mod's app: its exe and Run value name (the old app's, which it replaces), its icon, and the
# old app's single-instance mutex, so the new one won't run alongside it.
APPS = {
    "taskbar-audio-flipper": {"app": "AudioFlipper", "icon": "icons/audio-flipper.ico", "old_mutex": r"Local\AudioFlipper.SingleInstance"},
    "taskbar-rdp-launcher": {"app": "RDPLauncher", "icon": "icons/rdp-launcher.ico", "old_mutex": r"Local\RDPLauncher.SingleInstance"},
    "taskbar-snipper": {"app": "TaskbarSnipper", "icon": "icons/taskbar-snipper.ico", "old_mutex": ""},
}

WINDHAWK_COMPILER = r"C:\Program Files\Windhawk\Compiler\bin"


# ---- The mod's header and settings block

def read_mod(path):
    text = open(path, encoding="utf-8").read().replace("\r\n", "\n")
    header = text[text.index("// ==WindhawkMod=="):text.index("// ==/WindhawkMod==")]
    meta = {}
    for line in header.split("\n"):
        match = re.match(r"//\s*@(\S+)\s+(.*)$", line)
        if match:
            meta[match.group(1)] = match.group(2).strip()
    settings = ""
    start = text.find("// ==WindhawkModSettings==\n/*\n")
    if start >= 0:
        start += len("// ==WindhawkModSettings==\n/*\n")
        settings = text[start:text.index("*/\n// ==/WindhawkModSettings==", start)]
    return meta, settings


def wide_literal(text):
    """A C++ wide string literal, a line at a time."""
    if not text:
        return 'L""'
    parts = []
    for line in text.splitlines(keepends=True):
        escaped = line.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n")
        parts.append('L"' + escaped + '"')
    return "\n    ".join(parts)


def write_mod_info(path, mod_id, meta, settings, app):
    with open(path, "w", encoding="utf-8", newline="\n") as out:
        out.write("// Written by tools/build.py from mods/%s.wh.cpp.\n#pragma once\n\n" % mod_id)
        out.write("#define WH_MOD_ID %s\n" % wide_literal(mod_id))
        out.write("#define WH_MOD_VERSION %s\n" % wide_literal(meta["version"]))
        out.write("#define WHHOST_NAME %s\n" % wide_literal(meta["name"]))
        out.write("#define WHHOST_APP %s\n" % wide_literal(app["app"]))
        out.write("#define WHHOST_OLD_APP_MUTEX %s\n" % wide_literal(app["old_mutex"]))
        out.write("#define WHHOST_SETTINGS \\\n    %s\n" % wide_literal(settings).replace("\n    ", " \\\n    "))


# ---- Resources: a .res file, which the linker takes as it is

def res_entry(type_id, name_id, data, flags=0x1030, language=0x0409):
    header = struct.pack("<II", len(data), 32)
    header += struct.pack("<HHHH", 0xFFFF, type_id, 0xFFFF, name_id)
    header += struct.pack("<IHHII", 0, flags, language, 0, 0)
    blob = header + data
    return blob + b"\0" * (-len(blob) % 4)


def icon_resources(path):
    """RT_ICON for each image in the .ico, and the RT_GROUP_ICON listing them (as id 1)."""
    data = open(path, "rb").read()
    _, kind, count = struct.unpack_from("<HHH", data, 0)
    assert kind == 1, path + " isn't an icon"
    entries = []
    group = struct.pack("<HHH", 0, 1, count)
    for i in range(count):
        width, height, colors, reserved, planes, bits, size, offset = struct.unpack_from("<BBBBHHII", data, 6 + i * 16)
        entries.append(res_entry(3, i + 1, data[offset:offset + size]))  # RT_ICON
        group += struct.pack("<BBBBHHIH", width, height, colors, reserved, planes, bits, size, i + 1)
    entries.append(res_entry(14, 1, group))  # RT_GROUP_ICON
    return b"".join(entries)


def manifest(name, app, version):
    numbers = (version.split(".") + ["0"] * 4)[:4]
    return f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity type="win32" name="TaskbarWidgets.{app}" version="{'.'.join(numbers)}"/>
  <description>{name}</description>
  <dependency>
    <dependentAssembly>
      <assemblyIdentity type="win32" name="Microsoft.Windows.Common-Controls" version="6.0.0.0" processorArchitecture="*" publicKeyToken="6595b64144ccf1df" language="*"/>
    </dependentAssembly>
  </dependency>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}}"/>
    </application>
  </compatibility>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="asInvoker" uiAccess="false"/>
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
""".encode("utf-8")


def version_block(key, value=b"", value_length=0, kind=1, children=b""):
    """One VS_VERSIONINFO-style block: length, value length, type, key, value, children."""
    def pad(blob):
        return blob + b"\0" * (-len(blob) % 4)
    body = pad(struct.pack("<HHH", 0, value_length, kind) + (key + "\0").encode("utf-16-le"))
    if value:
        body = pad(body + value)
    body += children
    return struct.pack("<H", len(body)) + body[2:]


def version_resource(name, app, version, description):
    numbers = [int(n) for n in (version.split(".") + ["0"] * 4)[:4]]
    ms = (numbers[0] << 16) | numbers[1]
    ls = (numbers[2] << 16) | numbers[3]
    fixed = struct.pack("<13I", 0xFEEF04BD, 0x00010000, ms, ls, ms, ls, 0x3F, 0, 0x00040004, 1, 0, 0, 0)

    def string(key, value):
        data = (value + "\0").encode("utf-16-le")
        return version_block(key, data, len(value) + 1, 1)
    strings = b"".join([
        string("CompanyName", "Lucidic15"),
        string("FileDescription", name),
        string("FileVersion", version),
        string("InternalName", app),
        string("LegalCopyright", "MIT License"),
        string("OriginalFilename", app + ".exe"),
        string("ProductName", name),
        string("ProductVersion", version),
        string("Comments", description),
    ])
    string_file_info = version_block("StringFileInfo", children=version_block("040904b0", children=strings))
    translation = version_block("Translation", struct.pack("<HH", 0x0409, 0x04B0), 4, 0)
    var_file_info = version_block("VarFileInfo", children=translation)
    return version_block("VS_VERSION_INFO", fixed, len(fixed), 0, string_file_info + var_file_info)


def write_resources(path, meta, app):
    empty = struct.pack("<II", 0, 32) + struct.pack("<HHHH", 0xFFFF, 0, 0xFFFF, 0) + struct.pack("<IHHII", 0, 0, 0, 0, 0)
    data = empty
    data += icon_resources(os.path.join(ROOT, app["icon"]))
    data += res_entry(24, 1, manifest(meta["name"], app["app"], meta["version"]))  # RT_MANIFEST
    data += res_entry(16, 1, version_resource(meta["name"], app["app"], meta["version"], meta.get("description", "")))  # RT_VERSION
    open(path, "wb").write(data)


# ---- Compiling

def find_compiler(folder):
    candidates = [folder] if folder else [WINDHAWK_COMPILER, None]
    for candidate in candidates:
        if candidate is None:
            found = shutil.which("clang++")
            if found:
                return found
            continue
        path = os.path.join(candidate, "clang++.exe" if os.name == "nt" else "clang++")
        if os.path.exists(path):
            return path
    sys.exit("No clang++ found: install Windhawk, or pass --compiler with a folder holding LLVM MinGW's clang++.")


def build(mod_id, compiler, out_dir):
    app = APPS[mod_id]
    source = os.path.join(ROOT, "mods", mod_id + ".wh.cpp")
    meta, settings = read_mod(source)
    work = os.path.join(out_dir, mod_id)
    os.makedirs(work, exist_ok=True)
    write_mod_info(os.path.join(work, "mod_info.h"), mod_id, meta, settings, app)
    write_resources(os.path.join(work, "app.res"), meta, app)
    exe = os.path.join(out_dir, app["app"] + ".exe")
    command = [compiler]
    # Windhawk's Clang builds for any target; others build for their own.
    if "windhawk" in compiler.lower():
        command += ["-target", "x86_64-w64-mingw32"]
    command += [
        "-std=c++23", "-O2", "-municode", "-mwindows", "-static", "-s",
        "-DUNICODE", "-D_UNICODE", "-DWINVER=0x0A00", "-D_WIN32_WINNT=0x0A00", "-D_WIN32_IE=0x0A00",
        "-DNTDDI_VERSION=0x0A000008", "-D__USE_MINGW_ANSI_STDIO=0", "-DWHHOST",
        "-I", work, "-I", os.path.join(ROOT, "standalone"), "-include", "windhawk_api.h",
        "-Wall", "-Wextra", "-Wno-unused-parameter", "-Wno-missing-field-initializers",
        "-Wno-missing-designated-field-initializers", "-Wno-unknown-warning-option",
        "-x", "c++", source, os.path.join(ROOT, "standalone", "host.cpp"),
        "-x", "none", os.path.join(work, "app.res"),
        "-o", exe,
    ]
    command += meta.get("compilerOptions", "").split()
    command += ["-lcomctl32", "-lshell32", "-luser32", "-lgdi32", "-lole32", "-ladvapi32"]
    print("Building", os.path.relpath(exe, ROOT), "from", os.path.relpath(source, ROOT), "version", meta["version"])
    result = subprocess.run(command)
    if result.returncode != 0:
        sys.exit("Couldn't build " + mod_id)
    return exe


def main():
    parser = argparse.ArgumentParser(description="Builds the standalone apps from the Windhawk mods.")
    parser.add_argument("--compiler", help="a folder with LLVM MinGW's clang++ (default: Windhawk's)")
    parser.add_argument("--out", default=os.path.join(ROOT, "build"), help="where the apps go (default: build)")
    parser.add_argument("mods", nargs="*", help="mod ids to build (default: all)")
    args = parser.parse_args()
    compiler = find_compiler(args.compiler)
    for mod_id in args.mods or list(APPS):
        if mod_id not in APPS:
            sys.exit("No app for " + mod_id + "; the apps are: " + ", ".join(APPS))
        build(mod_id, compiler, args.out)


if __name__ == "__main__":
    main()
