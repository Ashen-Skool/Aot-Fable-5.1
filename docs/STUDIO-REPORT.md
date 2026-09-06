# Studio session report — 2026-09-05 evening

Autonomous session on the Mac Studio in `~/dev/lanes/director`, main, against the six tasks in
`docs/STUDIO-HANDOFF.md`. All six are done and pushed. `tools/test.sh` is green (39 tests: 16 EditMode,
23 PlayMode — three new camera tests). Every change was built (`tools/build.sh mac`) and judged from real
screenshots taken by the harness.

## 1. Ride camera — `eb3eb47`

The chase camera sat inside the Titan's back and the nape steam filled the frame.

- `CameraTargetState.Riding` (set by `OdmCameraTarget`) opens the chase to `rideDistance` 5 m and
  `rideHeight` 2.2 m, with his facing as the heading so the camera stays behind the neck. Free look is
  untouched (the mouse offset still rides on top of the heading).
- The Titan's colliders are skipped by `ResolveCollision` while riding, so his back cannot shove the
  camera in as he turns.
- `TitanFx` emits small (0.8 m), short (0.8 s) steam and a much weaker nape plume while `Ridden`; the red
  spray is never shrunk, since it is the stab's read.
- Test: `CameraRigTests.RidingPullsTheCameraBackAndUp`.

Verified: `shots/play/play_9_0s.png`, `play_11_0s.png` (a `-autoRide 8 -autoSlash 10` run) — she reads on
the nape with the blade up, the street ahead is visible, no steam wall. The "before" pair is kept in
`shots/ride_before/`.

## 2. Stabs read — `903cb9a`

- `CameraRig.Punch(amount)`: a 0.45 m lunge along the view plus a 2.2 deg tip, springing back in 0.11 s.
  `TitanBrain.Stab` calls it (0.6, or 0.9 on the killing stab) on top of the existing shake and hit-stop.
  Test: `CameraRigTests.PunchLungesAlongTheViewThenReturns`.
- The `napestab` clip is proven, not assumed: `CharacterModel.ActiveClipName` is logged on every stab.
  The log reads `[Ride] stab 1 clip=napestab` … `[Ride] stab 5 KILL clip=napefinal`, so the takes in
  `Mikasa.fbx.meta` are the ones playing, not the `thrustslash`/`chargedslash` alternates.
- The blood was HDR orange at 1.5 m and read as a cartoon starburst under bloom. It is now darker red
  (1.1, 0.09, 0.07), 0.05–0.13 m, shorter-lived and less stretched.

Verified: `shots/play/play_10_2s.png` — spray, "1 / 5", the first HUD dot filled.

## 3. Final plunge, cutscene, kill cam, YOU WON — `903cb9a`

`-autoStabs N` presses LMB N times, 0.6 s apart, from the `-autoSlash` time. The first version lost
presses: hit-stop stretches the ride's 0.5 s stab cooldown in real time, so presses 2–5 were swallowed and
the run stopped at four stabs. The harness now reads `OdmController.Stabs` by reflection and re-presses
(0.2 s later) any press that did not land, stopping when N have.

Whole chain verified in one run
(`tools/play.sh 42 -autoStart 2 -autoRide 8 -autoSlash 10 -autoStabs 5 -screenshotAt … -quitAfter 32`):

- five stabs then `[Ride] stab 5 KILL clip=napefinal` and `[TitanHit] NAPE KILL`
- the cutscene video: `shots/play/play_15_0s.png`, `play_21_5s.png` (nape.mp4 full screen)
- the kill cam: `[Cam] … mode=KillCam` in the `-fpslog` output between the cutscene and the ending
- YOU WON: `shots/play/play_23_0s.png`, `play_28_0s.png` (over his corpse and the death steam)

## 4. Wall perch play-test — `cee55fb`

`-autoPerch N` sweeps 24 yaws × 3 upward pitches for the nearest wall face (|normal.y| < 0.35, at least
6 m above her) and hooks it. That found three real bugs:

- the hook latch dropped at 3.5 m from the anchor, before `reelDetach` (2.2 m), so a real wall hook never
  reached `EnterPerch` at all. A wall anchor now holds the latch.
- she perched facing **out**, so the authored `wallperch` crouch (knees drawn up, feet forward) left her
  feet hanging in the air. She now faces the wall; her feet land on the face.
- the chase camera backed into the stone (a full-screen white smear of her own mesh). Perched, the camera
  swings out in front at `perchDistance` 4.5 m with a 30 deg three-quarter yaw. Test:
  `CameraRigTests.PerchPutsTheCameraInFrontOfHer`.

Two input fixes came out of the same pass: Space off a perch (or the nape) is consumed, or the hook toggle
read the same press and fired a fresh virtual anchor in the frame she let go; and a launch that would go
into the wall now keeps the component of the look along the face instead of collapsing to the normal, so
Shift still aims.

Verified: `shots/play/play_8_0s.png` (perch, cables up to both heads, feet on the wall, no clipping) and
`play_9_1s.png` / `play_9_3s.png` (LMB leap into the air attack).

## 5. wallkick — `a9b3778`

Authored (`blender -b -P tools/author_clips.py -- … wallkick`, 18 frames — the strip in `shots/author`
reads as a crouch to a push-off), merged into `Mikasa.fbx` (28 clips) and added to the `.meta`
`clipAnimations`. `ExitPerch(kick: true)` plays it; because the same LMB also starts the air attack, the
attack is held for `kickAttackDelay` 0.3 s and `UpdatePose` leaves the model alone for that long (`Fly`
replaced the clip on the next frame otherwise). Proven by `[Perch] exit kick=True clip=wallkick` followed
by `[Slash]` 0.3 s later.

## 6. Polish — `b047265`

- `CharacterModel.ShakeHead`: after each stab the Titan's `Head` bone snaps ±12 deg at 6 Hz, decaying over
  0.6 s (same LateUpdate pattern as the wrist roll). Called from `TitanBrain.Stab`.
- Five screenshots of a normal run (`-autoFly 6`, at 8/12/20/30/40 s) all had the same worst problem: the
  shadow side of the towers and walls was flat black over half the frame. The grade's contrast goes 10 → 6
  and the shadows get a 0.13 lift, so the stone reads while the sunlit side is unchanged.
  Before: the 30 s and 40 s frames of that run. After: `shots/play/play_8_0s.png`, `play_30_0s.png`.

## Open

- After the nape kill she falls to the ground in a stiff pose; the ending card lands ~2 s later so it is
  barely seen, but it is not right.
- The blood spray still reads as a small starburst at close range.
- The Titan never reached her in a 40 s `-autoFly` run at seed 42 (she ends up parked in an alley). Worth
  checking whether that is the harness leaving her still or the brain losing her.
- The Shift launch and Space drop off a perch are wired and code-checked but not play-tested from the
  harness (there is no flag for them); LMB is.
- One `tools/test.sh` EditMode run crashed the editor (exit 139) on the first import of the new
  `Mikasa.fbx`; the immediate re-run was green.
