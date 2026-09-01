# AOT FABLE 5.1 — the brief

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
