#!/usr/bin/env python3
"""Scan assets/staged and write assets/manifest.json. usage: build_manifest.py <repo> <results.jsonl>"""
import json, os, sys
from pathlib import Path

REPO = Path(sys.argv[1]); A = REPO / "assets"; S = A / "staged"
R = {}
for l in open(sys.argv[2]):
    r = json.loads(l)
    if "out" in r:
        R[Path(r["out"]).name] = r

SRC = {
    "kn": ("Kenney Fantasy Town Kit 2.0 (kenney.nl/assets/fantasy-town-kit)", "CC0-1.0"),
    "kc": ("Kenney Castle Kit (kenney.nl/assets/castle-kit)", "CC0-1.0"),
    "ks": ("Kenney Survival Kit (kenney.nl/assets/survival-kit)", "CC0-1.0"),
    "kk": ("KayKit Medieval Hexagon Pack 1.0 (github.com/KayKit-Game-Assets/KayKit-Medieval-Hexagon-Pack-1.0)", "CC0-1.0"),
    "qv": ("Quaternius Medieval Village (usda mirror github.com/weftspun/quaternius-stage)", "CC0-1.0"),
    "qm": ("Quaternius Medieval Village MegaKit (usda mirror github.com/weftspun/quaternius-stage)", "CC0-1.0"),
    "qp": ("Quaternius Fantasy Props MegaKit (usda mirror github.com/weftspun/quaternius-stage)", "CC0-1.0"),
    "qb": ("Quaternius Modular Medieval Buildings (usda mirror github.com/weftspun/quaternius-stage)", "CC0-1.0"),
}
UAL1 = ("Quaternius Universal Animation Library [Standard] (quaternius.itch.io/universal-animation-library)", "CC0-1.0")
UAL2 = ("Quaternius Universal Animation Library 2 [Standard] (quaternius.itch.io/universal-animation-library-2)", "CC0-1.0")
UBC = ("Quaternius Universal Base Characters [Standard] (quaternius.itch.io/universal-base-characters)", "CC0-1.0")
ACG = ("ambientCG (ambientcg.com/view?id={id}) 1K JPG", "CC0-1.0")
PH = ("Poly Haven (polyhaven.com/a/{id}) 2K HDR", "CC0-1.0")
KIMP = ("Kenney Impact Sounds (kenney.nl/assets/impact-sounds)", "CC0-1.0")
KUI = ("Kenney Interface Sounds (kenney.nl/assets/interface-sounds)", "CC0-1.0")

entries = []
def rel(p): return str(Path(p).relative_to(A))

for f in sorted((S / "town").glob("*.glb")):
    cat, tag, _ = f.name.split("-", 2)
    src, lic = SRC[tag]
    r = R.get(f.name, {})
    entries.append({"id": f.stem, "path": rel(f), "cat": "town/" + cat, "source": src, "license": lic,
                    "tris": r.get("tris"), "dims": r.get("dims"), "bytes": f.stat().st_size})

ANIM_NOTES = {
    "UAL1_Standard.glb": (UAL1, 43, "43 clips, root motion off; UE-style skeleton (root/pelvis/spine_01..)"),
    "UAL1_Standard_RM.glb": (UAL1, 43, "same 43 clips with root motion baked"),
    "UAL2_Standard.glb": (UAL2, 43, "43 clips (Sword_* combos, Slide_*, NinjaJump_*, ClimbUp_1m, Hit_Knockback...), root motion off"),
    "UAL2_Standard_RM.glb": (UAL2, 43, "same 43 clips with root motion baked"),
    "Mannequin_F.glb": (UAL2, 0, "female mannequin, same rig, no clips (retarget from UAL files)"),
}
for f in sorted((S / "anim").glob("*.glb")):
    r = R.get(f.name, {})
    if f.name in ANIM_NOTES:
        (src, lic), n, note = ANIM_NOTES[f.name]
        entries.append({"id": f.stem, "path": rel(f), "cat": "anim/library", "source": src, "license": lic,
                        "anims": n, "dims": r.get("dims"), "note": note, "bytes": f.stat().st_size})
    else:
        sub = "anim/character" if "FullBody" in f.name else "anim/hair"
        entries.append({"id": f.stem, "path": rel(f), "cat": sub, "source": UBC[0], "license": UBC[1],
                        "tris": r.get("tris"), "dims": r.get("dims"), "bytes": f.stat().st_size,
                        "note": "same 65-bone skeleton as UAL; hair rigged to Head bone" if sub == "anim/hair" else "rigged, T-pose, same 65-bone skeleton as UAL"})

TEXCAT = {"Bricks076A": "stone-wall", "Bricks089": "stone-wall", "Bricks096": "stone-wall", "Bricks100": "stone-wall",
          "RoofingTiles012A": "roof-tile", "RoofingTiles013A": "roof-tile", "PavingStones046": "cobblestone",
          "PavingStones131": "cobblestone", "Planks039": "wood-plank", "Plaster007": "plaster", "Ground103": "dirt", "Rock030": "rock"}
for d in sorted(p for p in (S / "textures").iterdir() if p.is_dir()):
    for m in ("color", "normal", "roughness"):
        f = d / f"{m}.jpg"
        entries.append({"id": f"tex-{d.name}-{m}", "path": rel(f), "cat": f"textures/{TEXCAT.get(d.name, 'misc')}",
                        "source": ACG[0].format(id=d.name), "license": ACG[1], "map": m,
                        "note": "normal is OpenGL (+Y); flip green for DirectX" if m == "normal" else None, "bytes": f.stat().st_size})

HDRI = {"qwantani_late_afternoon_puresky_2k.hdr": "golden late afternoon, clear, high contrast",
        "kloofendal_overcast_puresky_2k.hdr": "overcast, low contrast"}
for f in sorted((S / "hdri").glob("*.hdr")):
    pid = f.stem[:-3]
    entries.append({"id": f"hdri-{pid}", "path": rel(f), "cat": "hdri", "source": PH[0].format(id=pid), "license": PH[1],
                    "note": HDRI.get(f.name), "bytes": f.stat().st_size})

for sub, src in (("impact", KIMP), ("interface", KUI)):
    for f in sorted((S / "audio" / sub).glob("*.ogg")):
        entries.append({"id": f"sfx-{sub}-{f.stem}", "path": rel(f), "cat": f"audio/{sub}", "source": src[0], "license": src[1],
                        "format": "ogg vorbis mono 44.1k", "bytes": f.stat().st_size})

for e in entries:
    for k in [k for k, v in e.items() if v is None]:
        del e[k]
json.dump({"root": "assets/", "units": "meters, Y-up (glTF)", "count": len(entries), "entries": entries},
          open(A / "manifest.json", "w"), indent=1)
import collections
c = collections.Counter(e["cat"].split("/")[0] for e in entries)
print(len(entries), dict(c), "bytes", sum(e["bytes"] for e in entries))
