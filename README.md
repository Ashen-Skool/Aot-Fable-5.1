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
| Harness (piece 0) | **in progress on branch `lane/harness`** (Unity project, URP, capture rig, build/test scripts, progress page). The sub-agent building it dies with the session; pick up from the branch's last commit and `docs/HARNESS.md` if present, else finish per the Piece 0 spec in docs/GAUNTLET.md |
| Progress page | GitHub Pages enabled on main; `progress.html` at the site root once the harness lands |
| Concept art | `assets/concepts/` — user approved **mikasa-2.png** and **titan-2.png** |
| Meshy image-to-3D | submitted for both approved concepts. Task IDs below. Results expire ~3 days after completion; fetch with `GET https://api.meshy.ai/openapi/v1/image-to-3d/<id>` and download model_urls.glb |
| Waves 1+ | not started; gated on harness |

Meshy tasks: mikasa `01a05f0f-0ac8-7194-be07-89eb90e0a418` (MESHY_OK /private/tmp/claude-501/-Users-darkeatermidir/eb3e7b58-17a9-4ce7-ba95-0e67671b4c8f/scratchpad/meshy/mikasa ['model.fbx', 'model.glb', 'task.json', 'tex_base_color.png', 'tex_metallic.png', 'tex_normal.png', 'tex_roughness.png', 'thumb.png']), titan `01a05f0f-0ac9-762c-88b9-ffaa0b53d6b9` (MESHY_OK /private/tmp/claude-501/-Users-darkeatermidir/eb3e7b58-17a9-4ce7-ba95-0e67671b4c8f/scratchpad/meshy/titan ['model.fbx', 'model.glb', 'task.json', 'tex_base_color.png', 'tex_metallic.png', 'tex_normal.png', 'tex_roughness.png', 'thumb.png']).

## The one rule that is not in the docs

**Mikasa and the Titan models are made WITH the user, step by step.** He
approves every stage visually (concept → mesh turntable → finish pass → rig →
in-engine). Use `tools/turntable.py` on the Studio to render approval sheets and
send them to him. Sub-agents build everything else against the proxy rigs in
docs/BRIEF.md. Do not let a sub-agent touch the character models.

## Resuming

1. `git fetch`; look at every `lane/*` branch; merge what is green.
2. Finish or verify the harness; validate it (open the PNGs) before trusting a critic.
3. Poll the Meshy tasks; run turntables; get the user's approval.
4. Launch wave 1 (proxies, town, ODM flight, camera) with the prompts in docs/PROMPTS.md,
   max 4 builders, one critic each, loop until win.
5. Keep the user updated every 10 minutes and push after every merge.

## Tools

- `tools/meshy.py <image> <out> [--polycount N] [--pose a-pose]` — image to 3D, polls, downloads.
- `tools/turntable.py` — `blender -b -P tools/turntable.py -- model.glb out/ 8` renders 8 angles + head + stats.json.
