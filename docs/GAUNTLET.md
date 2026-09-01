# The gauntlet — how this repo gets built

Adapted from gaming-ralph-loops/GAUNTLET.md and the Red Sands post-mortem.

## Roles

- **Director** (the main session). Splits work, runs waves, owns `progress.html`,
  merges lanes, never judges pixels itself.
- **Builder** (sub-agent, one per piece per round). Owns one directory under
  `unity/Assets/<Piece>/`. Reads the brief, the last critic verdict, and the
  harness docs. Ships code, runs the capture, hands back nothing but a commit.
- **Critic** (sub-agent, fresh context, one per round). Gets: the brief line
  for the piece, the reference frames, and the freshly captured PNGs. Never the
  builder's summary, never the diff. Blind A/B against the reference: which is
  better? If ours loses, name the ONE biggest gap. Verdict is JSON in
  `gauntlet/<piece>/round-N.json`.
- **Smoother** (sub-agent, fresh, one per wave end). Plays the whole build,
  captures a full run, unifies pieces into one coherent thing. Not a redesign.

## The loop per piece

```
build → capture → critic → wins? done : (gap → build) …
```

No fixed round count. Safety cap: 8 rounds per piece per wave, then the
director looks. Lanes are git branches `lane/<piece>`; the director merges
into `main` after each wave.

## Budget law (Red Sands lesson 8)

- Max 4 builders in flight at once. A dead wave costs a shipped wave.
- Critics are cheap (images + one paragraph). Builders are not. A builder gets
  one gap per round, never a list.
- Any sub-agent that hits an error loop twice stops and reports.

## Harness contract

- `tools/capture.sh <piece|all> [pose…]` renders PNGs to `shots/<piece>/` on
  the Studio and syncs them to the director. Poses live in `tools/poses.json`.
- `tools/build.sh mac|webgl` produces `builds/mac/` or `builds/webgl/`.
- `tools/test.sh` runs EditMode + PlayMode tests in batch mode.
- `tools/progress.mjs` regenerates `progress.html` from `gauntlet/**/round-*.json`
  and the latest shots. Pushed to `main` after every round; served by GitHub
  Pages at `/progress.html`.
- Validate the harness before believing a verdict: a pose that points at
  nothing produces a critic that says the lighting is terrible.

## Engine contract (every builder)

1. You own `unity/Assets/<Piece>/`. Never edit outside it except
   `unity/Assets/Shared/` by director approval.
2. Cross-piece access through `Shared/Ctx.Get<T>("name")` only.
3. No new packages without director approval.
4. Seeded RNG only.
5. No per-frame allocation. Preallocate in Awake.
6. Never emit .meta by hand; let Unity generate them and commit them.
7. `tools/test.sh` and `tools/build.sh mac` must pass before you commit.
