# AOT FABLE 5.1

A ten-minute playable Attack on Titan homage in Unity 6, built through the CLI
on the Mac Studio by a gauntlet loop of builder and critic sub-agents. Started
2026-09-01. **Read this whole file before doing anything.**

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
| Waves 1+ | wave 1 launched 2026-09-01 evening: lanes proxies, town, odm, camera. Check `lane/*` branches and `gauntlet/` for round verdicts |


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
