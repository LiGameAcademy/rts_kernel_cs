# Traffic computation contract probe (experimental)

Issue #94. This is an isolated reference executable, not a replacement movement system. Production `Rts.Kernel`, its v11 snapshots and Godot host are unchanged. It reuses the approved raw32.32 values; arbitrary-precision `BigInteger` is an accuracy oracle, not a production collision implementation or performance result.

The revised gameplay direction is: sustained physical blockage at three seconds requests traffic replanning; repeated lack of useful progress eventually fails, with thresholds still to be assessed. Eligible members of the same group, including temporarily settled members, may voluntarily yield; Stop and successor orders take precedence. These rules have been approved as the target, but are not implemented in production by this probe.

## First probe: time-aligned linear movement

All proposals describe straight constant-speed motion over the same normalized frame interval. `LinearTimeCollision` evaluates relative movement continuously, with exact contact allowed. This is deliberately narrower than the real stepper's piecewise waypoint, turn and slope motion.

`FrameMotionResolver` first observes all proposals in one initial world, then removes conflicting movement deterministically (older waits, then entity ID). Each removal makes that body stationary, so dependent proposals are checked again. It is an intentionally simple bounded-size reference (up to 64), not an optimized solver. It never invents movement for a Stop body. Inputs and output lists remain independently owned.

Checks include the production two-unit convoy counterexample, safe crossings at different times, simultaneous crossing, exact contact and one raw unit overlap, 2/12/64 same-speed chains, Stop dependency propagation, iteration-order independence and randomized proposals. An independent signed one-dimensional interval test checks the geometric oracle rather than only reusing it for postconditions. Canonical results are pinned across two processes in Debug and Release.

The exchange case deliberately remains blocked: temporal safety does not generate a detour or guarantee progress. This is evidence for continuing the shared traffic-plan experiment, not permission to declare arrival quality solved.

```powershell
./experiments/TrafficPrototype/Test-Prototype.ps1 -ReportPath <local-report.json>
```

Not yet validated: continuous group coordination/yield plans, useful-progress thresholds, static navigation, unequal motion profiles, waypoint timing, snapshot continuation of long-lived traffic plans, 300/500 performance, or Godot visuals. The 64-member size is an early probe bound, not a reduction in the project's 500-member requirement.
