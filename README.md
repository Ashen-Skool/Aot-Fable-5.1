# AOT FABLE 5.1

Repo: https://github.com/Ashen-Skool/Aot-Fable-5.1 (moved from the personal account 2026-09-02). Progress page: https://ashen-skool.github.io/Aot-Fable-5.1/progress.html

A ten-minute playable Attack on Titan homage in Unity 6, built through the CLI
on the Mac Studio by a gauntlet loop of builder and critic sub-agents. Started
2026-09-01. **Read this whole file before doing anything.**

## STATE (2026-09-05, late, read this, the rest of this file is history)

Scope is v2: one district, one 15 m Titan, Mikasa, three-minute loop. No agent loops; the director builds by hand
and the user is the critic (he plays the mac build on his laptop; never screenshot or drive his machine).

**In the build, all on main:**
- Mikasa: Meshy mesh + auto-rig, 23 library clips (`docs/CLIPS.md`), random attack per click (ground set / air set),
  real ODM blade prop in gloved fists (`Resources/Props/Blade`, `Fist`), fist roll tunable live with `[` `]`
  (`CharacterModel.FistRollDeg`, user still has to give the number to bake in).
- Titan (boss): Meshy mesh + rig, `Proxies/TitanBrain.cs` chase / swipe / stomp (~9 m reach), 100 HP bar, zone damage
  (nape 40, or kill when kneeling after both hamstrings), death stays on the ground and shows the ending screen (R restarts).
- Player: `ODM/OdmController.cs`. Space toggles hooks (virtual anchor 45 m out if nothing is aimed at, auto pull, press again
  to release), Shift gas, WASD air steer, LMB attack, Esc frees the mouse, click captures it. 100 HP, knockback, respawn.
- Camera: `CameraRig/CameraRig.cs`, centered on her, free look at any speed, heading re-based (not eased) when steering on foot.
- Cannons: `Proxies/Cannon.cs`, placed by `ProxyBootstrap.CannonPlacer` on the 3 tallest flight-test towers (now permanent,
  stone-dressed) + the wall walkway. E to fire within 5 m, 40 dmg, HUD markers with distance. Tower placement verified in a capture (`tools/unity.sh ... -odmGrid 1`): three on tower tops, one on the wall.
- World: ground trimmed to the town bounds with invisible boundary walls; fall below -25 m respawns.
- Props pipeline: concept (Higgsfield) → `tools/meshy.py` → `tools/prop_finish.py` → `Resources/Props/<Name>.fbx` →
  `Shared/PropAlign.Align` orients from geometry at runtime (exporter axes are not trusted).

- World look (beauty pass 1, 2026-09-05): `wasteland_clouds_puresky` HDRI (sun az/el measured by `tools/sunpos.py`, brightness by
  `tools/skymean.py`), sky-lit ambient, soft 4k shadows, SSAO renderer feature (added by `UrpSetup.Ssao`), post volume in
  `TownRuntime.Grade` (ACES, bloom, white balance, split tone, vignette, grain), fog matched to the sky, and `Town/TownLife.cs`
  (chimney smoke, dust motes on the camera, two bird flocks). A quarter of the houses have lamp-lit windows (`HouseSpec.LitWindows`).
  Editor setup must run after pulling on the Studio: `tools/unity.sh townsetup -quit -executeMethod Town.Editor.TownSetup.Run` then
  `tools/setup.sh` (they rewrite Sky.mat, Particles.mat, UrpPipeline/UrpRenderer assets, which are then committed).
- Flight feel (2026-09-05): catenary cables with a whip on bite and a 4-frame shoot-out, grapple heads, anchor dust/sparks, camera kick
  on latch, twin hip gas jets, procedural wind + gas-hiss beds (`Shared/NoiseLoop.cs`), blade trails during a slash, FOV widens with
  speed (`CameraRig.speedFovAdd`).
- HUD: `ODM/Hud.cs` (Bebas Neue / Oswald in `Resources/Fonts`): crosshair + hook-target ring with range, HP + segmented gas, speed,
  Titan bar with hamstring chips and NAPE OPEN, floating damage numbers (`Shared/HudEvents`), cannon markers, hit vignette, FPS
  counter top right, title and ending screens. R on the ending screen rebuilds the world through `Shared/Reboot` (boots register
  with a priority) instead of reloading an empty scene. **Reboot is untested in a real build.**
- Titan presentation: `Proxies/TitanFx.cs` (steam + red spray at cuts, nape plume when opened, stomp dust ring + rubble, step and
  impact camera trauma, death steam, steam noise), hit-stop (`Shared/HitStop`) on heavy hits, kill cam on the nape kill, music ducks on hits.
- Titan balance: 1.05 s telegraph with a SWIPE/STOMP warning at the strike point, hits only land in his front arc, damage 24/32,
  softer knockback, 2.6 s cooldown, player has 1.6 s of grace after a hit (user said he was getting cooked on approach).
- World layer 2: `Town/TownDressing.cs` (forest + hills on a dark meadow beyond the boundary, gutters, puddles, dirt, hay, moss at
  the wall foot, hanging shop signs, weathervanes, pigeons, lamp glow + point lights, gate torches with flame and flicker, raised
  portcullis), wall blocks in three shades, ground mist bank (`TownLife.Mist`), `Shared/AmbientBed.cs` (wind gusts, synthesized bell).
  Ground103 is now imported under `Town/Imported/Resources/Town/Textures` (it was missing; ground materials fell back to bricks).
  Outskirt ground uses `TownMaterials.TexturedSimple` (Simple Lit) and a dark tint; from the rooftops it still lifts toward the fog.
- Capture poses `outskirts` and `wall_top` added to `tools/poses.json`.
- **Performance: the 26 fps was vsync.** `Bootstrap` forces `vSyncCount = 0`; the Studio then runs 150-360 fps at 1080p with everything on.
  Town is static-batched at runtime (`TownRuntime`), dressing/outskirts cast no shadows, the Titan has a kinematic Rigidbody.
- **Player self-checks** (`Shared/Harness.cs`, all command-line): `-quitAfter N`, `-autoRestart N` (proved `Reboot` in a real build:
  RESTART_OK), `-fpslog`, `-autoStart N` (lifts the title), `-autoKill N` (nape hit through reflection), `-screenshotAt a,b,c`
  (real frames with the HUD to `shots/play/`). `tools/play.sh 42 -fpslog -autoStart 2 -autoKill 26 -screenshotAt 15,27 -quitAfter 30`
  then rsync `shots/play/` back. `Shared/PerfToggles.cs`: `-noSsao -noPost -msaa1 -shadow2k -noShadows -noMist -noSmoke -noDust
  -noTrees -noLamps -noHud -noTown -noChars` for bisecting frame cost.
- Title: live orbit over the district from boot (`CameraRig.TitleOrbit`), click = intro dive to Mikasa (`BeginIntroDive`), input held
  and the Titan waits at the gate (`Ctx titleHold` / `introUntil`) then roars (`Shared/Synth.cs`: roar, whoosh). Boss starts at z=98.
- Soft camera lock on the Titan within 80 m (`Ctx cameraLockTarget`, set by `TitanBrain`). He now closes to stomp range.
- Verified from screenshots: title orbit, dive, HUD, approach with lock, swipe wind-up, kill cam + steam + damage number, ending card.
- Music: `Shared/Music.cs` crossfades `Resources/Audio/Music/{title,battle,ending}`; files not yet present (Suno).
- Audio: `Shared/Sfx.cs` pooled one-shots over `Resources/Audio` (Kenney): hooks, landing, slash, hits, titan steps and attacks, cannon.
- Title screen (click to begin, Time.timeScale 0 until then) and ending screen live in `OdmController.OnGUI`.

**Open items:** Suno music files; building destruction is rubble/dust only; attic hatches skipped;
fist roll number from the user; draw/sheathe pose; the tower grid visually swallows the town from above
(user asked for cannons on those towers, so they stay; a smaller grid or fewer towers is a design call for him);
music; the 7 m titan is a grey proxy on purpose.

**Process notes:** the Studio working copy that actually builds is the director lane `~/dev/lanes/director` (on main); set
`AOT_STUDIO_REPO=dev/lanes/director` for every tool. The old `~/dev/aot-fable-5.1` clone is stale and blocked by dirty Unity files.
Captures rewrite `shots/*/latest.png` on the Studio, so `git checkout -- shots/` there before every pull.
Keep `tools/test.sh` green (36 tests incl. `CharactersWiredTests`, `GroundSnapProbe`); build with
`tools/build.sh mac`, then rsync the app to `~/Desktop/AOT-build/` on the laptop and `open` it for him. Unity batch runs
sometimes leave a stale editor holding the project lock after an interrupted command: `pkill -f lanes/director`.

## Read in this order

1. `docs/BRIEF.md` — what the game is, the 15 pieces, the bar (the real A.O.T. 2).
2. `docs/GAUNTLET.md` — builder/critic process, budget law, worktree-per-lane rule.
3. `docs/PROMPTS.md` — the exact builder, critic and smoother prompts.
4. `docs/HARNESS.md` — how to capture, build, test (exists once lane/harness merges).

## Where things run

- **Everything heavy runs on the Mac Studio**, reached by `ssh studio` (alias in the
  user's SSH config; works over Tailscale from any network). The MacBook only
  orchestrates. Never print IPs or hostnames in replies: the user streams.
- Studio clone: `~/dev/aot-fable-5.1` (director's, stays on main). Builders use
  worktrees at `~/dev/lanes/<lane>`. The Studio can push to GitHub via gh.
- Unity 6000.3.20f1: `/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity`.
  Licensed through Unity Hub sign-in on the Studio. If batch mode says
  "No valid Unity Editor license", ask the user to re-sign into Hub on the Studio.
  WebGL module installed. Probe WebGL build: 40 s, 5 MB.
- Blender 5.2 at `/opt/homebrew/bin/blender`. yt-dlp, ffmpeg, node 26, python3 present.
- Secrets: `~/.claude/secrets/meshy.key` on both machines (Meshy API, ~1000 credits).
  Higgsfield CLI is authenticated on the MacBook (`higgsfield account status`).

## State at handoff (2026-09-01)

| Item | State |
|---|---|
| Brief, gauntlet, prompts | on main |
| CC0 assets (town kits, textures, HDRIs, Quaternius anim library, Kenney audio) | on main under `assets/staged/`, manifest at `assets/manifest.json`. Missing: hero SFX (no Freesound key), music |
| Reference frames (critique only, gitignored) | on the Studio at `references/` (100 frames, 11 folders, `INDEX.md`, contact sheets). Regenerate with yt-dlp if lost; see INDEX for sources |
| Harness (piece 0) | MERGED to main. `docs/HARNESS.md` documents setup (13 s), capture (12 s / 4 shots), tests (19 s), progress page. The agent died on the monthly spend limit right after writing the docs, but the director then verified both: mac build 31 s (75 MB app), WebGL 148 s (15.5 MB). Fully green |
| Progress page | GitHub Pages enabled on main; `progress.html` at the site root once the harness lands |
| Concept art | `assets/concepts/` — user approved **mikasa-2.png** and **titan-2.png** |
| Meshy meshes | DONE, committed under `assets/characters/<name>/meshy-raw/` (GLB+FBX+PBR, ~31k tris, both 1.9 m tall). Turntables in `assets/characters/<name>/turntable/sheet.jpg`. User said they look good for now, provisional until seen in-engine |
| Wave 1 | **all four builders died on the monthly spend limit** (second time; limit resets 1:40am Chicago on 2026-09-02). State: `lane/proxies` round 1 pushed + critic verdict in `gauntlet/proxies/round-1.json` (lose, 6/10, gap = titan poses indistinguishable; round 2 was in progress, WIP in `wip/proxies` if present). Town, ODM, camera never pushed a lane commit; their uncommitted worktree state is snapshotted as `wip/town`, `wip/odm`, `wip/camera` (stash-style commits: restore with `git checkout <wip-branch> -- .` inside a fresh worktree on main). Town's tests were passing at death; ODM and camera status unknown |


## The one rule that is not in the docs

**Mikasa and the Titan models are made WITH the user, step by step.** He
approves every stage visually (concept → mesh turntable → finish pass → rig →
in-engine). Use `tools/turntable.py` on the Studio to render approval sheets and
send them to him. Sub-agents build everything else against the proxy rigs in
docs/BRIEF.md. Do not let a sub-agent touch the character models.

## Resuming

1. `git fetch`; look at every `lane/*` branch; merge what is green.
2. Finish or verify the harness; validate it (open the PNGs) before trusting a critic.
3. Character finish pass WITH the user: scale Titan to 15 m, Mikasa to 1.70 m; decimate if needed; cel-shade materials; auto-rig (Meshy rigging API or Blender Rigify/UBC skeleton retarget from `assets/staged/anim/`); export GLB into `unity/Assets/Characters/`. He approves each stage from a turntable render.
4. Launch wave 1 (proxies, town, ODM flight, camera) with the prompts in docs/PROMPTS.md,
   max 4 builders, one critic each, loop until win.
5. Keep the user updated every 10 minutes and push after every merge.

## Tools

- `tools/meshy.py <image> <out> [--polycount N] [--pose a-pose]` — image to 3D, polls, downloads.
- `tools/turntable.py` — `blender -b -P tools/turntable.py -- model.glb out/ 8` renders 8 angles + head + stats.json.
