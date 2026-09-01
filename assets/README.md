# assets/ — staged CC0 assets for AOT FABLE 5.1

Everything under `staged/` is CC0 1.0 (see `ATTRIBUTION.md`; nothing here needs
credit). `manifest.json` has one entry per file (`id, path, cat, source,
license, tris, dims, bytes`); game code should load by manifest id and fall
back to a primitive when a file is missing. Total ~89 MB.

Units: metres, Y-up glTF binaries, origin at the base of the object (min y = 0).
Every GLB was re-exported through headless Blender 5.2 with a scale factor and
its bounding box measured after scaling (`dims` = [width, height, depth]).
The scripts that did it are in `tools/` (`stage_town.py` builds the job list,
`convert2.py` runs inside Blender, `inspect.py` prints dims/tris for any file).

## staged/town/ — 501 GLB, 37 MB

File name = `<category>-<source>-<original-name>.glb`. Categories: house, wall,
roof, chimney, window, gate, tower, stall, cart, barrel, crate, well, fountain,
fence, lamp, stairs, road, tree, rock, prop. Source tags and the scale applied:

| tag | pack | scale | why |
|---|---|---|---|
| `kn` | Kenney Fantasy Town Kit 2.0 (all 167 pieces) | x3 | 1 tile = 1 storey; wall pieces are 3 m, lantern 3.7 m, cart 2.7x4 m |
| `kc` | Kenney Castle Kit (74 pieces, no ground tiles) | x4 | wall segment 5.2 m, gate 3.6 m, tower ring 4 m |
| `ks` | Kenney Survival Kit (24 props) | x3 | barrel 1.03 m, box 0.75 m, fence 1.55 m |
| `kk` | KayKit Medieval Hexagon Pack (whole buildings, walls, props) | x9 buildings, x3.5 well, x4 walls/props | home 8.4 m, tavern 12.6 m, church 14.8 m, towers 20-22 m, barrel 0.85 m |
| `qv` | Quaternius Medieval Village (whole houses + props) | x2.5 (small props x4) | house 5-8.5 m, inn 8.7 m, mill 12 m, well 3.1 m |
| `qm` | Quaternius Medieval Village MegaKit (modular walls/roofs/props, PBR textures) | x1 | already real scale: wall module 2 x 3.12 m, chimney 3 m, wagon 4 m |
| `qp` | Quaternius Fantasy Props MegaKit (barrels, crates, stalls, lanterns...) | x1 | already real scale: barrel 0.9 m, stall 2.6 m |
| `qb` | Quaternius Modular Medieval Buildings (town wall, gate, towers, well) | x3 | tall wall 7 m, gate 7 m, towers 6-16 m |

Quality tiers: `qm`/`qp` are the only ones with real PBR textures (embedded
512 px JPEG; the ambientCG sets below are the intended hero textures). `kn`,
`kc`, `ks`, `kk` are flat-colour low-poly; `qv`/`qb` are vertex/material
coloured. For Shiganshina the tall-house look comes from stacking `qm` wall
modules (3.12 m each) under a `qm` roof, or from the `kk`/`qv` whole houses
for the far background.

## staged/textures/ — 12 ambientCG sets, 1K JPG, 6.9 MB

`color.jpg`, `normal.jpg` (OpenGL +Y; Unity wants this), `roughness.jpg`
only. Stone wall x4: `Bricks076A` (dirty medieval), `Bricks089` (dark grey
medieval), `Bricks096` (dark grey old), `Bricks100` (beige old stone).
Roof tile x2: `RoofingTiles012A`, `RoofingTiles013A`. Cobblestone x2:
`PavingStones046`, `PavingStones131`. Wood `Planks039`, plaster `Plaster007`
(old, broken white), dirt `Ground103`, rock `Rock030`. Some sets are 1024x512.

## staged/hdri/ — 2 Poly Haven skies, 2K .hdr, 8.4 MB

`qwantani_late_afternoon_puresky_2k.hdr` (golden late afternoon, clear) and
`kloofendal_overcast_puresky_2k.hdr` (overcast).

## staged/anim/ — Quaternius Universal Animation Library + Base Characters, 35 MB

The most valuable folder. All files share one 65-bone UE-style skeleton
(`root, pelvis, spine_01..03, neck_01, Head, clavicle_l, upperarm_l ...`), so
clips retarget between them directly, and onto our own rig via Unity's
Humanoid avatar.

- `UAL1_Standard.glb` / `UAL1_Standard_RM.glb`: 43 clips (Idle, Walk, Jog,
  Sprint, Jump_Start/Loop/Land, Roll, Crouch, Hit_Chest/Head, Death01,
  Sword_Idle/Attack, Punch, Sitting, Pistol, Spell, Swim...). `_RM` = root
  motion baked in.
- `UAL2_Standard.glb` / `UAL2_Standard_RM.glb`: 43 more (Sword_Regular_A/B/C
  + combos, Sword_Heavy_Combo, Sword_Block/Dash, Slide_Start/Loop/Exit,
  NinjaJump_*, ClimbUp_1m, Hit_Knockback, Melee_Hook, OverhandThrow, Shield_*,
  Zombie_*...). These are the Mikasa blade set.
- `Mannequin_F.glb`: female mannequin on the same rig, no clips.
- `UBC_Superhero_Female_FullBody.glb` / `UBC_Superhero_Male_FullBody.glb`:
  rigged base bodies, 1.78 / 1.82 m, 1K JPEG textures.
- `UBC_Hair_*_rigged.glb`, `UBC_Eyebrows_*_rigged.glb`: hair pieces skinned to
  the Head bone.

Note: the official UAL GLBs are committed untouched (not re-exported, so the
animation data is exactly as shipped). The UBC files were re-exported through
Blender to shrink the 2K PNG textures to 1K/512 JPEG. When you open any of
these rigged files in Blender 5.2 the importer adds a 2 m `Icosphere` object
of its own; it is not in the files (verified by reading the glTF JSON).

Only the free `[Standard]` tiers are here. The 250+-clip UAL "Pro" and the
UBC "Source" are paid on itch.io and were not fetched.

## staged/audio/ — Kenney, 230 OGG (mono, 44.1 kHz, vorbis q4), 1.9 MB

`impact/` (130): footsteps on concrete/wood/grass/snow/carpet, impactMetal /
Plate / Wood / Plank / Soft / Punch / Glass / Bell / Mining / Tin at light /
medium / heavy. `interface/` (100): UI clicks, confirmations, errors.

## Missing / not done

- **Freesound**: no API key on this machine, so none of the brief's hero
  SFX were fetched: gas hiss, cable whip/twang, blade whoosh, blade-into-flesh,
  giant footstep thud, giant roar, wind rush, stone rubble collapse. Kenney
  `impactSoft_heavy_*` / `impactPunch_heavy_*` / `impactPlate_heavy_*` are
  weak stand-ins for hits and footsteps. Get a Freesound key
  (freesound.org/apiv2/apply), query with `filter=license:"Creative Commons 0"`.
- **poly.pizza**: no API key, skipped.
- **Music**: nothing staged.

## Rejected

- `pmndrs/market-assets`: 13 GLBs, none medieval (cloned, nothing taken).
- Quaternius `UltimateTexturedBuildings`: modern city (AC units, casino
  and pharmacy signs) despite the promising `2Story/4Story` names.
- Kenney `city-kit-suburban` / `city-kit-commercial`, KayKit
  `City-Builder-Bits`: modern.
- Kenney `medieval-kit`: does not exist.
- Quaternius usda mirror copies of UAL/UAL2/UBC: see below.
- KayKit Medieval Hexagon `hills/mountain/tree` tiles, flags, targets: hex
  terrain tiles, not town.
- Kenney castle-kit `ground*` tiles.

## Playbook (ASSETS.md) corrections

1. **`weftspun/quaternius-stage` is useless for animation.** Its
   `UniversalAnimationLibrary*/SK_*.usda` files are 147-line skinned meshes with
   zero `SkelAnimation` prims. Its `UniversalBaseCharacters` usda files import
   fine but drag in 2K PNG textures per file (5-9 MB per hair piece). The
   sparse-checkout recipe itself works and the static packs (MedievalVillage,
   MedievalVillageMegaKit, FantasyPropsMegaKit, ModularMedievalBuildings) are
   good; `blender --python` USD import in Blender 5.2 handles them.
2. **Working Quaternius recipe: itch.io, no login.** Free tiers download
   anonymously; the GLBs are already in the zip (`Unreal-Godot/*.glb`):
   ```
   page=https://quaternius.itch.io/universal-animation-library
   csrf=$(curl -sL -c j -b j $page | grep -oE 'name="csrf_token" value="[^"]+"' | head -1 | sed 's/.*value="//;s/"$//')
   url=$(curl -sL -c j -b j -X POST -d "csrf_token=$csrf" $page/download_url | python3 -c 'import sys,json;print(json.load(sys.stdin)["url"])')
   uid=$(curl -sL -c j -b j "$url" | grep -oE 'data-upload_id="[0-9]+"' | head -1 | grep -oE '[0-9]+')
   file=$(curl -sL -c j -b j -X POST -d "csrf_token=$csrf" "$page/file/$uid?source=view_game&as_props=1" | python3 -c 'import sys,json;print(json.load(sys.stdin)["url"])')
   curl -sL -o pack.zip "$file"   # signed URL expires in 60 s
   ```
   Upload ids: UAL 17958403, UAL2 17958478, UBC 15861669.
   GitHub fallback for UAL1 only: `IAFahim/quaternius.universalAnimationLibrary.standard`
   (a 25 MB FBX, 45 clips, CC0 LICENSE in the repo).
3. **KayKit**: `api.github.com/orgs/KayKit-Game-Assets/repos` 404s; the repos
   exist but carry a version suffix (`KayKit-Medieval-Hexagon-Pack-1.0`,
   `KayKit-Dungeon-Remastered-1.0`, `KayKit-City-Builder-Bits-1.0`...). Use the
   search API or clone by full name. Models are `.gltf` + `.bin` + one colormap.
4. **Kenney recipe works unchanged** (5/5 packs). Kits are at "1 tile = 1 unit"
   toy scale, not metres: expect x3-x4.
5. **ambientCG recipe works**; search via
   `https://ambientcg.com/api/v2/full_json?q=<q>&type=Material&limit=N&include=tagData,downloadData`.
6. **Poly Haven works**: `api.polyhaven.com/assets?t=hdris`, then
   `api.polyhaven.com/files/<id>` -> `.hdri.2k.hdr.url`.
7. Blender 5.2's glTF **importer** adds an `Icosphere` mesh object whenever it
   imports a skinned file. Do not trust Blender-side tri counts / bounds for
   rigged GLBs; read the glTF JSON (`meshes[]`) to know what is really inside.
