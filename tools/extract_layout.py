#!/usr/bin/env python3
"""Recover the Azeron configurator's button layout from the shipped AppImage.

The profile JSON the app writes to disk has no geometry in it -- x/y are zero on
every input. The real layout is a CSS grid-template-areas string inside the
Electron renderer bundle, one per keypad model. This pulls it out and writes the
Layout/*.json the plugin embeds, so the plugin itself never touches the AppImage.

    python3 tools/extract_layout.py ~/AppImages/azeron-software.appimage

Needs 7z on PATH (to get resources/app.asar out of the AppImage's squashfs).
Everything it unpacks goes to a temp dir and is removed on the way out.
"""
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(os.path.dirname(HERE), "AzeronHud", "Layout")

# Models we emit. Keyed by the renderer's KeypadType enum value, which is also
# what lands in a profile's metaData.device.
MODELS = {8: ("cyborg2", "Azeron Cyborg II", 4855)}


class Asar:
    """Minimal reader for Electron's asar container (header pickle + JSON index)."""

    def __init__(self, path):
        self.f = open(path, "rb")
        _, pickle_size, _, json_len = struct.unpack("<IIII", self.f.read(16))
        self.header = json.loads(self.f.read(json_len).decode("utf-8"))
        self.base = 8 + pickle_size

    def walk(self, node=None, prefix=""):
        node = self.header if node is None else node
        for name, meta in node.get("files", {}).items():
            path = f"{prefix}/{name}" if prefix else name
            if "files" in meta:
                yield from self.walk(meta, path)
            else:
                yield path, meta

    def read(self, meta):
        self.f.seek(self.base + int(meta["offset"]))
        return self.f.read(meta["size"])


def renderer_bundle(asar):
    """The one big minified JS file under out/renderer/assets."""
    best = None
    for path, meta in asar.walk():
        if path.startswith("out/renderer/assets/") and path.endswith(".js"):
            if best is None or meta["size"] > best[1]["size"]:
                best = (path, meta)
    if best is None:
        sys.exit("no renderer bundle found in app.asar")
    return asar.read(best[1]).decode("utf-8", "replace")


# A grid-template-areas value: consecutive quoted rows of "bNN" / "." tokens.
ROW_RE = re.compile(r'"(?:\s*(?:b\d+|\.)\s+)+(?:b\d+|\.)\s*"')


def grid_templates(js):
    """Every grid-template-areas literal in the bundle, as lists of row strings.

    Rows that sit next to each other in the source belong to the same template,
    so group on adjacency rather than trying to parse the surrounding JS.
    """
    out, current, prev_end = [], [], None
    for m in ROW_RE.finditer(js):
        # Rows of one template are separated only by `\n` + indentation.
        if prev_end is not None and not re.fullmatch(r"[\s\\n]*", js[prev_end:m.start()]):
            out.append(current)
            current = []
        current.append(m.group()[1:-1].split())
        prev_end = m.end()
    if current:
        out.append(current)
    return [g for g in out if len(g) > 1]


def keypad_template(js, keypad_type):
    """The right-handed template for one keypad type.

    The bundle has a `Ud(type, isLefty)` switch mapping each KeypadType to a pair
    of minified identifiers. Read the switch so we pick the correct variant
    instead of guessing by position.
    """
    sw = re.search(r"case \$e\.(\w+):return (\w+)\?(\w+):(\w+);", js)
    if sw is None:
        sys.exit("could not locate the keypad -> template switch")
    cases = dict(
        (name, (lefty, right))
        for name, _flag, lefty, right in re.findall(
            r"case \$e\.(\w+):return (\w+)\?(\w+):(\w+);", js
        )
    )
    # Scope the enum lookup to the KeypadType declaration -- several unrelated
    # enums in the bundle reuse the same small integers.
    decl = re.search(r"\(e=>\((?:e\[e\.\w+=\d+\]=\"\w+\",)*e\[e\.CyborgV2=\d+\][^)]*\)", js)
    if decl is None:
        sys.exit("could not locate the KeypadType enum")
    enum = dict(
        (int(v), k) for k, v in re.findall(r"e\[e\.(\w+)=(\d+)\]", decl.group())
    )
    name = enum.get(keypad_type)
    if name not in cases:
        sys.exit(f"no right-handed template case for keypad type {keypad_type} ({name})")
    ident = cases[name][1]
    m = re.search(re.escape(ident) + r"=`([^`]*)`", js)
    if m is None:
        sys.exit(f"template identifier {ident} not found")
    rows = [r.strip().strip('"').split() for r in m.group(1).strip().splitlines()]
    return [r for r in rows if r]


def cell_aspect(js):
    """Button cells are width x width*qp; qp is a literal ratio in the bundle."""
    m = re.search(r"\bqp=(\d+)/(\d+)\b", js)
    if m is None:
        sys.exit("could not find the button aspect ratio")
    return int(m.group(1)), int(m.group(2))


def to_layout(rows, aspect, name, keypad_type, product_id):
    """Collapse the area grid into one placed rect per button id."""
    spans = {}
    for r, row in enumerate(rows):
        for c, cell in enumerate(row):
            if cell == ".":
                continue
            bid = int(cell[1:])
            s = spans.setdefault(bid, {"col": c, "row": r, "col2": c, "row2": r})
            s["col"] = min(s["col"], c)
            s["row"] = min(s["row"], r)
            s["col2"] = max(s["col2"], c)
            s["row2"] = max(s["row2"], r)

    buttons = [
        {
            "id": bid,
            "col": s["col"],
            "row": s["row"],
            "colSpan": s["col2"] - s["col"] + 1,
            "rowSpan": s["row2"] - s["row"] + 1,
        }
        for bid, s in sorted(spans.items())
    ]
    h, w = aspect
    return {
        "name": name,
        "keypadType": keypad_type,
        "productId": product_id,
        "columns": max(len(r) for r in rows),
        "rows": len(rows),
        "cellAspect": h / w,
        "buttons": buttons,
    }


def main():
    if len(sys.argv) != 2:
        sys.exit(f"usage: {sys.argv[0]} <azeron-software.appimage>")
    appimage = os.path.expanduser(sys.argv[1])
    if not os.path.isfile(appimage):
        sys.exit(f"no such file: {appimage}")
    if shutil.which("7z") is None:
        sys.exit("7z is required to unpack the AppImage")

    tmp = tempfile.mkdtemp(prefix="azeron-layout-")
    try:
        subprocess.run(
            ["7z", "x", appimage, f"-o{tmp}", "resources/app.asar", "-y"],
            check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        )
        js = renderer_bundle(Asar(os.path.join(tmp, "resources", "app.asar")))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    aspect = cell_aspect(js)
    os.makedirs(OUT_DIR, exist_ok=True)
    for keypad_type, (slug, name, product_id) in MODELS.items():
        rows = keypad_template(js, keypad_type)
        layout = to_layout(rows, aspect, name, keypad_type, product_id)
        dst = os.path.join(OUT_DIR, f"{slug}.json")
        with open(dst, "w") as fh:
            json.dump(layout, fh, indent=2)
            fh.write("\n")
        print(f"{dst}: {len(layout['buttons'])} buttons, "
              f"{layout['columns']}x{layout['rows']} grid")

    # Handy to keep around: the numeric input-type enum the profiles use.
    types = re.search(r"\(e\.KeyboardKey=\"1\",[^)]*\)", js)
    if types:
        pairs = re.findall(r"e\.(\w+)=\"(\d+)\"", types.group())
        print(f"input types: {len(pairs)} values "
              f"(1={dict((v, k) for k, v in pairs)['1']})")


if __name__ == "__main__":
    main()
