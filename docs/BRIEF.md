# AOT FABLE 5.1 — the brief

> ## SCOPE v2 (2026-09-02) — READ THIS FIRST, IT OVERRIDES THE REST
> Budget reality: the gauntlet loop below burned half a week's plan on one wave.
> The shipping target is now **one scene, three minutes**: one district block,
> one 15 m Titan, Mikasa. Fly, cut both hamstrings, cut the nape, kill cam,
> results screen. Health, gas, timer, title and death screens. Golden HDRI plus
> one cel outline pass. A handful of Kenney clips for audio. Mac build to play on
> the Studio, WebGL to GitHub Pages.
>
> **Process v2:** no parallel builders, no critic agents, no scheduled ticks.
> The director builds directly, one piece at a time, on the Studio, and the
> USER is the critic (send him a shot, fix the one thing he names). Sub-agents
> only for something that is truly parallel and cheap. Character models are
> still made with the user step by step.
>
> **Cut for now:** 7 m titans (5), destruction (9), the ten-minute script (13),
> most of look (10) and audio (12), performance beyond 60 fps on the Studio (14).
> Everything below stays as the long-term reference only.

A ten-minute playable Attack on Titan homage built in Unity 6 through the CLI.
You are Mikasa. ODM gear, twin blades, a walled town, Titans. The bar is the
actual Attack on Titan console games (A.O.T. 2 / Wings of Freedom): the feel
of ODM flight, the speed, the camera, the blade impact, the cinematography.
Not "good for AI". As good as the game, side by side, blind.

Everything is original work. No ripped assets, no trademarked art. Reference
frames are used only for critique, never shipped.

## The ten minutes

1. **Cold open (0:00–0:40).** Title over a matte-painted wall skyline. Press
   any key. Camera dives off the wall into the district with Mikasa already
   in flight. Tutorial prompts fade in as you fly: hook, boost, swing, land.
2. **First blood (0:40–3:30).** Two 7 m Titans wander the market streets.
   Learn the loop: hook a rooftop, boost, swing behind, cut the nape. Each
   kill is a slow-motion camera beat with steam.
3. **The breach (3:30–5:00).** Wall gate explodes. A 15 m Abnormal (the boss)
   charges in, sprinting, unpredictable, and starts wrecking the district
   chasing you. Buildings crumble on impact.
4. **The hunt (5:00–9:00).** The boss fight. Cut both hamstrings to drop it to
   a knee, four-second nape window, miss and it stands faster and angrier.
   It grabs, swipes, stomps, throws debris, slams into buildings. Health bar,
   gas bar, blade durability, three lives.
5. **The kill (9:00–10:00).** Nape cut triggers a kill camera: orbit, freeze,
   steam eruption, Mikasa lands on a roof, scarf in the wind. Results screen
   with time, kills, damage taken. Restart.

## Hard requirements

- Unity 6000.3.20f1, URP, built and tested through the CLI only. No editor
  GUI in the loop. Content is code-driven: scenes bootstrap from C#, UI is
  built from code, materials generated at runtime or by editor scripts.
- Playable at 60 fps at 1080p on the Mac Studio in a macOS standalone build;
  WebGL build ships to GitHub Pages as the public deliverable.
- Keyboard + mouse. Gamepad is a bonus, not required.
- Deterministic capture: any camera pose in `tools/poses.json` can be rendered
  headlessly to a PNG at any time. The critic only ever looks at these.
- The whole thing lives in this repo. Nothing on the Studio that is not
  committed is real.

## The pieces (each judged alone)

| # | Piece | Judged on |
|---|---|---|
| 0 | Harness | Capture, build, test, progress page all work before anyone believes a verdict |
| 1 | Town | Reads as Shiganshina: tall stone houses, tiled roofs, chimneys, market, the wall, atmosphere, scale |
| 2 | ODM flight | Hook, reel, swing arc, gas boost, momentum, landing. Feel over physics accuracy |
| 3 | Camera | Chase cam, FOV kick on boost, speed lines, shake on impact, kill cam orbit |
| 4 | Mikasa | Model, rig, idle/run/flight/slash poses, scarf, blades, ODM rig on the hips |
| 5 | Titan (7 m) | Model, walk, grab, stagger, nape zone, steam death |
| 6 | Boss Titan (15 m) | Model, sprint, swipe, grab, stomp, debris throw, hamstring/nape zones, kneel |
| 7 | Combat | Slash arcs, hit detection on zones, hitstop, blood-steam, blade wear, nape cut |
| 8 | Titan AI | Perception, pathing through streets, chase, attack selection, building collision damage |
| 9 | Destruction | Buildings crumble on boss impact, debris physics, dust |
| 10 | Look | Cel outline, color grade, bloom, motion blur, sky, fog, time of day |
| 11 | HUD + screens | Health, gas, blades, timer, cut-target markers, title, death, win, results |
| 12 | Audio | Gas hiss, cable, wind, blades, titan roar, footsteps that shake, music, mix |
| 13 | Encounter | The ten-minute script above, pacing, tutorial prompts, difficulty curve |
| 14 | Performance | 60 fps standalone, WebGL under 60 MB, no per-frame allocation |

## Controls

WASD move · Mouse look · RMB hold: fire hooks at aim point (both anchors) ·
Space hold: gas boost along cable · Shift: reel in · LMB: slash · Q: swap
blades · Ctrl: dodge · Esc: pause.

## Proxy rigs (piece 15) and the model swap contract

Mikasa and both Titans are modelled and rigged by the director with the user,
later. Everything else is built against **proxies**: articulated humanoids
made from primitives with the exact bone names and zone colliders the final
models will carry, so swapping the art is a mesh swap, not a rewrite.

- `Shared/Rigs/HumanoidProxy.cs` builds a Unity Humanoid-compatible hierarchy
  (Hips, Spine, Chest, Neck, Head, L/R UpperArm/LowerArm/Hand, L/R
  UpperLeg/LowerLeg/Foot) from capsules, scaled by a height parameter.
- Titan proxies add zone colliders as children with these exact names:
  `Zone_Nape`, `Zone_HamstringL`, `Zone_HamstringR`, `Zone_ArmL`,
  `Zone_ArmR`, `Zone_Eyes`. Combat and AI only ever talk to zones.
- Mikasa's proxy adds `Socket_HookL`, `Socket_HookR` (hips), `Socket_BladeL`,
  `Socket_BladeR` (hands), `Socket_Scarf` (neck).
- Animation goes through `Shared/Rigs/IPoser` (Idle, Run, Fly, Slash, Land,
  Stagger, Kneel, Swipe, Grab, Stomp, Sprint). Proxies implement it
  procedurally; the final rigs implement it with Animator clips. Callers never
  reference clips.
