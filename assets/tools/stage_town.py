#!/usr/bin/env python3
"""Build the town-kit + anim job list and run it through headless Blender in parallel.
usage: python3 stage_town.py <work_dir> <out_root> [jobs_per_proc]
"""
import json, os, re, subprocess, sys, glob
from pathlib import Path

W = Path(sys.argv[1]); OUT = Path(sys.argv[2]); N = int(sys.argv[3]) if len(sys.argv) > 3 else 8
BLENDER = "/opt/homebrew/bin/blender"
TOOLS = W / "tools"

CATS = [  # (category, regex on lowercase stem) first match wins
    ("gate", r"gate|door|tunnel|entrance"),
    ("tower", r"tower|belltower"),
    ("stall", r"stall|marketstand"),
    ("house", r"house|inn$|home|tavern|church|blacksmith|stable|sawmill|windmill|watermill|barracks|lumbermill|mill$|archeryrange|gazebo|story|market|mine"),
    ("chimney", r"chimney"),
    ("roof", r"roof"),
    ("cart", r"cart|wagon|wheelbarrow|wheel"),
    ("barrel", r"barrel"),
    ("crate", r"crate|^box|chest|package|bag|sack|pallet|pouch"),
    ("well", r"well"),
    ("fountain", r"fountain"),
    ("fence", r"fence|hedge|poles"),
    ("lamp", r"lantern|lamp|torch|candle"),
    ("stairs", r"stair"),
    ("window", r"window|shutters"),
    ("wall", r"wall|balcony|overhang|cornerexterior"),
    ("road", r"^road|path|floor"),
    ("tree", r"tree|hills|mountain"),
    ("rock", r"rock"),
]

def cat_of(stem):
    s = stem.lower()
    for c, rx in CATS:
        if re.search(rx, s):
            return c
    return "prop"

def slug(stem):
    s = re.sub(r"^(S_|SK_)", "", stem)
    s = re.sub(r"[^A-Za-z0-9]+", "-", s).strip("-").lower()
    return s

jobs = []
def add(src, tag, scale, texmax=0, jpeg=False, cat=None, sub="town"):
    stem = Path(src).stem
    c = cat or cat_of(stem)
    out = OUT / sub / f"{c}-{tag}-{slug(stem)}.glb"
    jobs.append({"src": str(src), "out": str(out), "scale": scale, "texmax": texmax, "jpeg": jpeg,
                 "cat": c, "tag": tag})

# --- Kenney fantasy-town-kit: everything, x3 (1 unit = 1 storey tile) ---
for f in sorted(glob.glob(str(W / "kenney/fantasy-town-kit/Models/GLB format/*.glb"))):
    add(f, "kn", 2.4 if Path(f).stem == "lantern" else 3.0)
# --- Kenney castle-kit: everything but ground tiles, x4 ---
for f in sorted(glob.glob(str(W / "kenney/castle-kit/Models/GLB format/*.glb"))):
    if Path(f).stem.startswith("ground"):
        continue
    add(f, "kc", 4.0)
# --- Kenney survival-kit subset x3 ---
KS = "barrel barrel-open box box-large box-open box-large-open chest bucket fence fence-doorway fence-fortified signpost signpost-single workbench-anvil workbench campfire-pit tree-log tree-log-small resource-planks resource-wood resource-stone rock-a rock-b rock-c".split()
for n in KS:
    add(W / f"kenney/survival-kit/Models/GLB format/{n}.glb", "ks", 3.0)
# --- KayKit medieval hexagon pack (red set + neutral + props) ---
KK = W / "kaykit-medhex/addons/kaykit_medieval_hexagon_pack/Assets/gltf"
for n in "home_A home_B market tavern church blacksmith windmill barracks lumbermill tower_A tower_B mine".split():
    add(KK / f"buildings/red/building_{n}_red.gltf", "kk", 9.0)
add(KK / "buildings/red/building_well_red.gltf", "kk", 3.5)
for f in sorted(glob.glob(str(KK / "buildings/neutral/*.gltf"))):
    s = Path(f).stem
    if re.search(r"wall|fence|stage|destroyed|grain", s):
        add(f, "kk", 4.0)
for f in sorted(glob.glob(str(KK / "decoration/props/*.gltf"))):
    s = Path(f).stem
    if re.search(r"flag|target|weaponrack", s):
        continue
    add(f, "kk", 4.0)
# --- Quaternius MedievalVillage x2.5 (small props x4) ---
QV = W / "quaternius-stage/models/MedievalVillage"
for f in sorted(glob.glob(str(QV / "*.usda"))):
    s = Path(f).stem
    if re.search(r"Smoke|Path|Window", s):
        continue
    sc = 4.0 if re.search(r"Barrel|Crate|Bag|Package|Bench", s) else 2.5
    add(f, "qv", sc)
# --- Quaternius MedievalVillageMegaKit subset x1, textures 512 jpeg ---
QM = W / "quaternius-stage/models/MedievalVillageMegaKit"
QM_RX = [r"^S_Wall", r"^S_RoofRoundTiles(4x6|6x8|8x10|6x12)$", r"^S_RoofDormerRoundTile$", r"^S_RoofTowerRoundTiles$",
         r"^S_RoofFrontBrick[468]$", r"^S_RoofWooden2x1(Center|Corner|Middle)?$", r"^S_RoofLog$", r"^S_RoofSupport2$",
         r"^S_RoofFrontSupports$", r"^S_Overhang(Plaster|UnevenBrick)(Corner|CornerFront|Long|Short)$", r"^S_OverhangRoof",
         r"^S_Balcony", r"^S_CornerExterior(Brick|Wood|WideBrick|WideWood)$", r"^S_Door(1Round|2Flat|4Round)$", r"^S_DoorFrame",
         r"^S_Window(ThinRound1|WideFlat1|WideRound1|ThinFlat1|Roof)", r"^S_WindowShutters(Thin|Wide)Round(Open|Closed)$",
         r"^S_Prop(Chimney|Chimney2|Crate|Wagon|WoodenFence|MetalFence|Support|Brick[1-4]|Vine[12]|ExteriorBorder)",
         r"^S_StairsExterior(Straight|Sides|Platform|SingleSide)$"]
for f in sorted(glob.glob(str(QM / "*.usda"))):
    s = Path(f).stem
    if any(re.search(rx, s) for rx in QM_RX):
        add(f, "qm", 1.0, 512, True)
# --- Quaternius FantasyPropsMegaKit subset x1 ---
QP = W / "quaternius-stage/models/FantasyPropsMegaKit"
QP_N = "Barrel BarrelApples BarrelHolder CrateWooden CrateMetal FarmCrateApple FarmCrateCarrot FarmCrateEmpty StallEmpty StallCartEmpty BucketWooden_1 BucketMetal Cauldron LanternWall TorchMetal Rope_1 Rope_2 Rope_3 Bag PouchLarge Bench Stool Anvil AnvilLog Workbench Banner_1 Banner_2 Dummy ShieldWooden ChainCoil Pot_1 Vase_2 Vase_4 VaseRubbleMedium TableLarge PegRack Chair_1".split()
for n in QP_N:
    add(QP / f"S_{n}.usda", "qp", 1.0, 512, True)
# --- Quaternius ModularMedievalBuildings subset x3 (town wall + gate + towers) ---
QB = W / "quaternius-stage/models/ModularMedievalBuildings"
for n in "TallWall TallWallBricks TallWallEntrance Wall WallBricks WallEntrance WallEntranceBricks Watchtower WatchTowerWRoof LargeSquareTower SmallSquareTower Tower PointyTower Well Bridge Door Tunnel".split():
    add(QB / f"S_{n}.usda", "qb", 3.0)

# --- Quaternius Universal Base Characters (official gltf) -> anim/ ---
UB = W / "itch/Universal Base Characters[Standard]"
for n in ["Superhero_Female_FullBody", "Superhero_Male_FullBody"]:
    jobs.append({"src": str(UB / f"Base Characters/Godot - UE/{n}.gltf"), "out": str(OUT / "anim" / f"UBC_{n}.glb"),
                 "scale": 1.0, "texmax": 1024, "jpeg": True, "cat": "character", "tag": "ubc", "drop": "^Icosphere"})
for f in sorted(glob.glob(glob.escape(str(UB / "Hairstyles/Rigged to Head Bone/glTF (Godot -Unreal)")) + "/*.gltf")):
    jobs.append({"src": f, "out": str(OUT / "anim" / f"UBC_{Path(f).stem}_rigged.glb"),
                 "scale": 1.0, "texmax": 512, "jpeg": True, "cat": "character", "tag": "ubc", "drop": "^Icosphere"})

missing = [j for j in jobs if not os.path.exists(j["src"])]
for m in missing:
    print("MISSING", m["src"])
jobs = [j for j in jobs if os.path.exists(j["src"])]
print("jobs:", len(jobs))
(W / "conv").mkdir(exist_ok=True)
json.dump(jobs, open(W / "conv/jobs.json", "w"), indent=1)
results = W / "conv/results.jsonl"
if results.exists():
    results.unlink()
chunks = [jobs[i::N] for i in range(N)]
procs = []
for i, ch in enumerate(chunks):
    p = W / f"conv/jobs_{i}.json"
    json.dump(ch, open(p, "w"))
    log = open(W / f"conv/log_{i}.txt", "w")
    procs.append(subprocess.Popen([BLENDER, "--background", "--python", str(TOOLS / "convert2.py"), "--", str(p), str(results)],
                                  stdout=log, stderr=subprocess.STDOUT))
for p in procs:
    p.wait()
done = [json.loads(l) for l in open(results)]
errs = [d for d in done if "error" in d]
print("done:", len(done), "errors:", len(errs))
for e in errs:
    print("ERR", e["src"], e["error"][:200])
