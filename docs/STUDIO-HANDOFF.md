# Studio handoff (autonomous session, 2026-09-05 evening)

You are Claude Code running ON the Mac Studio in `~/dev/lanes/director` (branch `main`). The user is on a flight with no
internet; the laptop session has handed the work to you. Nobody will answer questions. Work autonomously, commit small, push
to `origin main` after every verified step, and stop cleanly when the list is done or after ~6 hours of work.

Read `README.md` (the STATE block at the top is the source of truth) and `docs/BRIEF.md` first. Everything runs from the CLI:
`tools/test.sh`, `tools/build.sh mac`, `tools/play.sh <seed> <flags>` (harness flags in `Shared/Harness.cs` and
`Shared/PerfToggles.cs`, screenshots land in `shots/play/`). Blender is `/opt/homebrew/bin/blender`. You are on the Studio, so
the tools run locally (`on_studio` is true); do NOT ssh anywhere and do NOT touch the laptop. Never run `git push --force`,
never rewrite history, never delete files you did not create. Before every pull: `git checkout -q -- .` (Unity import churn).

Verify visually: after each change build, run the harness with `-screenshotAt`, open the PNGs (Read tool) and judge them.

## Tasks, in order

1. **Ride camera.** During the nape ride the chase camera sits inside the nape steam and the Titan's back (see
   `shots/play/play_11_0s.png` from `tools/play.sh 42 -autoStart 2 -autoRide 8 -autoSlash 10 -screenshotAt 9,11 -quitAfter 13`).
   Make the ride readable: pull the camera back/up while `OdmController.Riding` (a ride distance in `CameraRig`, e.g. 6 m behind and
   2.5 m above her, looking at the nape), and shrink the nape steam / hit burst while ridden so it does not cover the frame.
   Keep free look. Verify with screenshots at 9 and 11 s.

2. **Stabs read.** Each `RideStab` should show: a red spray at the nape (`TitanFx.HitBurst` is there), a screen kick
   (`CameraRig` Shake 0.25), a short hit-stop (already), and the HUD dot filling. Add a subtle camera punch and make sure the
   `napestab` clip actually plays (log `[Ride] stab n` exists; check `CharacterModel` picks `napestab`, not an alternate:
   `Resources/Characters/Mikasa.fbx.meta` lists the takes; Unity only sees takes in `clipAnimations`).

3. **Final plunge + cutscene.** Drive the whole thing from the harness: `-autoRide 8 -autoSlash 10` only presses once; add
   `-autoStabs N` (N presses, 0.6 s apart, starting at the autoSlash time) to `Shared/Harness.cs` so five stabs happen, then verify
   the final plunge, the cutscene video (nape.mp4), the kill cam, and YOU WON with screenshots. Fix anything that breaks.

4. **Wall perch play-test.** Add `-autoPerch N` to the harness: at N seconds fire a hook straight at the nearest tall wall/tower
   face from the spawn (use `OdmController.Play(FlightScript...)` or a direct `TryHook` toward a raycast hit on `OdmLayers.HookMask`
   with |normal.y| < 0.35) so the reel ends in `EnterPerch`. Screenshot the perch from the chase camera. Fix the offsets
   (`EnterPerch`: 0.55 m out, 1.9 m down) so her feet sit on the wall and the cables run up to the anchor without clipping through
   the wall. Make sure LMB from the perch leaps into the air attack, Shift launches toward the look, Space drops.

5. **wallkick clip.** `tools/author_clips.py` has a `wallkick` definition that was never exported (the last runs only passed
   `wallperch,naperide,napestab,napefinal`). Run
   `blender -b -P tools/author_clips.py -- assets/characters/mikasa/rig shots/author wallkick`, look at the strip in `shots/author`,
   then re-merge: `blender -b -P tools/merge_clips.py -- assets/characters/mikasa/rig unity/Assets/Resources/Characters/Mikasa.fbx 1.70 idle walking_glb_url running_glb_url jump land slash combo hit combatidle swordrun weaponcombo bladespin spinjump parry runfast ropehang jumpcatch chargedslash thrustslash leftslash upslash weaponcombo2 axespin wallperch wallkick naperide napestab napefinal`,
   add a `wallkick` entry to `Mikasa.fbx.meta` `clipAnimations` (copy the `napestab` block, new random negative `internalID`,
   `lastFrame: 18`, `loopTime: 0`), and play it for ~0.3 s in `ExitPerch(kick: true)` before the air attack takes over
   (`CharacterModel.PlayClip("wallkick")`). Commit the GLB, FBX and meta.

6. **Polish pass (only after 1–5 are pushed):** the Titan while `Ridden` should stagger every stab (he does) and roar on odd
   stabs (he does) — add a head shake: rotate his `Head` bone via `CharacterModel` LateUpdate like the wrist roll, ±12° at 6 Hz,
   decaying over 0.6 s after each stab. Then look at five random screenshots of a normal run
   (`tools/play.sh 42 -autoStart 2 -autoFly 6 -screenshotAt 8,12,20,30,40 -quitAfter 42`) and fix the ugliest thing you see.

## Rules

- `tools/test.sh` must stay green (36 tests). Add a test when you change camera or brain logic.
- Commit message style: short imperative summary + a line on why. Push after each task.
- Update the README STATE block at the end (what shipped, what is verified, what is open) and push.
- Write a `docs/STUDIO-REPORT.md` summarizing what you did, what you verified (with screenshot filenames), and what is open.
- Do not open Unity's GUI. Do not change project settings assets unless a task requires it.
