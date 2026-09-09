# Agent server — the Unity half of the bridge

This folder turns *Yum Jump* into something an external program can play deterministically: a
loopback TCP server that hands out the level geometry and the live world state, and runs
keystroke plans a tick at a time. The client half lives in `Yum-Jump-LLM/` and does all the
modelling and planning; everything here exists so that the client's assumption — identical
inputs produce identical outcomes — actually holds.

Nothing in here runs during normal keyboard play. `SimClock.ManualMode` is false, gameplay
scripts fall through to Unity's own `Update()`, and `GameInput` reads the keyboard.

```
Sim/      the clock, the tick-driven base class, the registry, the event channel
Input/    the input seam (keyboard | agent) and the agent's virtual keys
Server/   the TCP server, plan runner, world sampler, reset controller, bootstrap, JSON
Editor/   the Tools menu that flips it on
```

## Turning it on

Three switches, checked by `AgentBootstrap` at `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`:

- **Editor:** `Tools > Agent Server > Enabled in Play Mode` (a `PlayerPrefs` flag —
  `YumJump.AgentServer`). `Tools > Agent Server > Set Port...` stores the port next to it.
- **Command line:** `-agentServer`, optionally `-agentPort 7777` and `-agentLevel Level1`.
  With `-agentLevel` the requested scene is loaded first and the server starts in *that*
  scene, so a headless build can be pointed at any level.
- **Environment:** `YUMJUMP_AGENT=1`, for platforms where passing arguments is awkward.

If none of them is set, `AgentBootstrap.Init` returns immediately and the game is unchanged.
When one is, it creates a bare `AgentServer` GameObject in code — no scene or prefab edits,
matching the project's convention of building runtime objects in C#.

The server binds `IPAddress.Loopback` only (default port `7777`, `AgentServer.DefaultPort`),
and logs `[AgentServer] ready on 127.0.0.1:<port> ...` once the world has been snapshotted.

## The stepping model

`AgentServer.Awake` does three things before anything can move: `SimClock.EnterManualMode`,
`Physics2D.simulationMode = SimulationMode2D.Script`, and `GameInput.Source = virtualInput`.
From that moment Unity is no longer allowed to advance the world on its own.

A frame in agent mode looks like this (`[DefaultExecutionOrder(-10000)]` guarantees the
server's `Update`/`LateUpdate` bracket everyone else's):

```
Update      DrainCommands()                  handle whatever arrived on the socket
            executor.WantsTick()?            no  -> nothing happens this frame
            SimClock.BeginTick()
            executor.BeginTick()             virtual keys applied for this tick
            SimRegistry.TickAll()            every SimBehaviour.SimTick(), sorted order
LateUpdate  Physics2D.Simulate(FixedDelta)   the one physics step
            SimRegistry.SampleVelocities()   derived velocity for transform movers
            SimClock.EndTick()               Tick++
            ResetController.ProcessPendingCapture()
            executor.EndTick()               record the trace row, test for terminals
```

Two properties fall out of this, and both are load-bearing.

**The world is frozen unless a job is running.** `PlanExecutor.WantsTick()` is the only thing
that authorises a tick, and it is false unless a `run_plan` or an `observe` still has ticks
left. Between commands the game sits at a known tick with known state; `get_state` is a pure
read. A client can take as long as it likes to think without the level moving underneath it.

**Exactly one tick per rendered frame.** This is the part worth defending. Plenty of engine
behaviour is quantized to the *frame*, not to the physics step: `Destroy` takes effect at the
end of the frame, coroutines resume on frame boundaries, `Animator` evaluation is per frame.
If a tick were allowed to run more than once per frame, those things would land in different
relative positions at different speeds and replays would drift. Pinning the tick to the frame
makes all of it behave identically no matter how fast the process is running.

Which is why **speed is a frame-rate target, never a bigger step**. `set_speed` sets
`Application.targetFrameRate = tickRateHz * scale` (and `vSyncCount = 0`); `SimClock.FixedDelta`
never changes. `scale` is capped at 8 and plans are counted in ticks, so speed changes how long
you wait and nothing else. `SimClock.Time` is derived as `Tick * FixedDelta` — the clock is
the tick counter, not the other way round.

## The invariants

The client refers to these by number (`I1`–`I5`); so do the code comments.

**I1 — ticks are the only unit of time.** Nothing on the wire is measured in seconds or
milliseconds. `SimClock.Time` and `SimClock.DeltaTime` are the single time source for gameplay
code, and in manual mode they are derived from the tick counter. `SimClock.Wait(seconds)`
converts to whole ticks up front (`SimWait`), so a coroutine that waits "0.6 s" waits exactly
36 ticks at 60 Hz, every time. Enforced in `Sim/SimClock.cs`; the surviving `WaitForSeconds`
calls are all in paths that never run in agent mode (the level-complete popup, the menu demo
player, UI flashes).

**I2 — input is applied at the top of the tick, before any gameplay logic.**
`PlanExecutor.BeginTick()` runs in `AgentServer.Update` *before* `SimRegistry.TickAll()`, and
`VirtualInput.ApplyForTick` computes the jump edge itself (`jump && !jumpWasHeld`) rather than
sampling `Input.GetKeyDown`. A step of `{"keys":["right"],"ticks":20}` therefore means twenty
gameplay steps and twenty physics steps with the key genuinely held, and a jump fires on
exactly one tick. Enforced in `Input/VirtualInput.cs` and `Server/PlanExecutor.cs`.

**I3 — reset restores the player *and* every dynamic object, with the tick counter zeroed.**
Restoring only the player would be useless: the second run of a plan would meet the saws at a
different phase. `ResetController.Initialise` snapshots the authored world on the first
`Update` (after every `Start` has run), each `SimBehaviour` contributes transform, rigidbody,
collider-enabled flags and its own `CaptureExtra()` state, and `Reset` destroys runtime spawns,
restores the snapshot, restores `GameManager` progress, zeroes `SimClock` and the spawn-id
counter, and re-instantiates the player. Checkpoints get the same treatment — `Checkpoint`
calls `ResetController.RequestCheckpointCapture`, which defers the snapshot to the end of the
tick, because the trigger fires from inside the physics step and a mid-step snapshot would
capture a state no tick boundary ever had. Enforced in `Server/ResetController.cs`.

**I4 — idle means frozen.** `AgentServer.Update` steps nothing unless `executor.WantsTick()`,
and physics only runs from `LateUpdate` when a tick actually happened. Nothing else in the
project may call `Physics2D.Simulate`. Enforced in `Server/AgentServer.cs`.

**I5 — the server reports raw state and never interprets it.** `WorldSampler` classifies
*geometry* (solid, one-way, hazard, deadzone) because that is a fact about colliders; it says
nothing about what a saw is going to do next. Dynamic objects go out as `objId`, `kind`,
`pos`, `vel`, `aabb` and nothing else. `SimEvents` is a report channel — game scripts say
"death, cause `hazard:saw_2`" and the server turns that into a terminal without deciding what
it means. Classification and prediction live in the client's `dynamics.py`. Enforced in
`Server/WorldSampler.cs` and `Sim/SimEvents.cs`.

## The protocol

JSON lines over TCP: one UTF-8 JSON object per line, `\n`-terminated, request and response.
Requests are flat — `{"id": 7, "cmd": "observe", "ticks": 120}`, not a nested params object.
Every response echoes the request `id` and carries either `result` or `error`:

```json
{"id":7,"result":{"startTick":0,"endTick":120,"ticksRun":120,"trace":[],"events":[]}}
{"id":8,"error":{"code":"busy","message":"a plan or observe is already running"}}
```

A line that will not parse gets an error with `"id": 0`, since the id could not be read.
Floats are rounded to four decimals (`Json.FloatDigits`); `Vector2` is written as `[x, y]`.
`run_plan` and `observe` do not answer until the job finishes, which is what makes a
one-request-at-a-time client the natural shape.

| Command | Request fields | Result |
|---|---|---|
| `hello` | — | `protocolVersion`, `tickRateHz`, `keys`, `levelName`, `gridCellSize`, `speed`, `tick`, `maxPlanTicks`, and `levelBounds` once the map has been sampled |
| `ping` | — | `pong`, `tick` |
| `get_static_map` | `cellSize` (optional; re-samples if it differs from the cached one) | `cellSize`, `origin`, `width`, `height`, `levelName`, `rows`, `legend`, `finishCell`, `markers`, `staticHazards`, `terrain`, `levelBounds` |
| `get_state` | `localWidth` (40), `localHeight` (24), `radius` (0 = no filter), `includeGrid` (true) | `tick`, `player`, `dynamics`, `localGrid` |
| `observe` | `ticks` (clamped to 1…3000) | `startTick`, `endTick`, `ticksRun`, `trace`, `events`, `player`, optional `warning` |
| `run_plan` | `plan`: `[{keys, ticks}, …]` | `startTick`, `endTick`, `ticksRun`, `terminal`, `deathCause`, `checkpoint`, `trace`, `events`, `finalState` |
| `reset` | `to`: `"spawn"` (default) or `"checkpoint:<id>"` | a full `get_state` result plus `resetTo` |
| `set_speed` | `scale` in (0, 8] | `scale`, `targetFrameRate` |
| `abort` | — | `aborted`, `tick`, and `job` (the partial result of the job that was cut short) when there was one |

Keys are `left`, `right`, `up`, `down`, `jump`. `up`/`down` drive the vertical axis (`down` is
the fast wall slide); a plan step's `ticks` must be ≥ 1 and the whole plan must total no more
than `PlanExecutor.MaxPlanTicks` (6000).

`abort` and `reset` both terminate a job that is in flight, and one request still produces
exactly one response: the cut-short `run_plan`/`observe` does *not* answer on its own id.
`abort` carries its partial result — `terminal: "aborted"`, a `note` of `"aborted by client"`,
and the trace collected up to that point — as the `job` field of its own response. `reset`
drops it, because the world that trace belongs to is being thrown away; its response is the
usual state snapshot. `aborted` is false when there was nothing to abort.

### Errors

| Code | When |
|---|---|
| `bad_json` | the line did not parse (answered with `id: 0`) |
| `unknown_command` | no such `cmd` |
| `busy` | a plan or observe is already running |
| `bad_key` | a plan step named a key outside the supported set |
| `bad_plan` | empty plan, a step with `ticks < 1`, or a plan over the tick limit |
| `no_player` | `run_plan` while the player is dead — reset first |
| `bad_reset` | unknown checkpoint (it has not been reached in this session) |
| `bad_speed` | `scale` outside (0, 8] |
| `internal_error` | an exception escaped the handler; also logged with a stack trace |

### The static map

`WorldSampler` overlaps every cell of a grid covering the level (default 0.5 u) and classifies
the hits by rank — what kills you outranks what you stand on, which outranks decoration. That
ranking is not cosmetic: flag triggers reach down into the floor, and a map that reported that
floor as a flag would have the client planning to walk through it.

The probe is `Physics2D.OverlapArea` over the **whole** cell, closed on every edge:
`[origin + cell * cellSize, origin + (cell + 1) * cellSize]`, the same arithmetic the client
uses to turn a cell back into world space. Sampling anything smaller leaves an unsampled band
straddling every cell boundary, and on a tilemap that is exactly where the walls are — see
"walls live on the cell boundaries" below.

`rows` is emitted **top row first**; internally `y = 0` is the bottom. `origin` is the world
position of the grid's lower-left corner, so a cell's centre is
`origin + (cell + 0.5) * cellSize`. Cells outside the sampled area read as `#` in the local
grid, so the client cannot plan into the void.

| Char | Meaning | | Char | Meaning |
|---|---|---|---|---|
| `.` | empty | | `S` | start |
| `#` | solid | | `o` | collectible |
| `=` | one-way platform | | `T` | trampoline |
| `^` | static hazard | | `W` | fan updraft |
| `X` | deadzone (kills on touch) | | `p` | falling platform, initial position |
| `F` | finish | | `@` | player *(local grid only)* |
| `C` | checkpoint | | `*` | moving hazard *(local grid only)* |
| | | | `e` | enemy *(local grid only)* |
| | | | `-` | moving platform *(local grid only)* |

The last four are overlays stamped onto the window returned by `get_state`; they never appear
in the full static map, which is sampled once and cached.

`markers` reports start/checkpoint/finish positions separately from the grid, because a marker
sitting inside a solid cell is invisible on the map (geometry outranks it) and the client still
needs to know where it is. `staticHazards` reports the exact AABB of every kill volume that
*stays where it is* — spikes, dead zones, soft respawn zones — because the grid rounds a spike up
to whole cells, which is fine to look at and far too pessimistic to plan against.

**Anything that moves is not in it.** A `Trap_Saw` carries a `DamageTrigger` exactly like a spike
does, so collecting triggers alone reported every patrolling saw as permanent geometry at
whatever position it held on the tick the map happened to be sampled — a box with the wrong
lifetime *and* an arbitrary position, which a client reads as a stretch of corridor that is
lethal for ever. Moving hazards are reported per tick in `get_state`'s `dynamics`, with position,
velocity and extents, and that is the only place they appear. The test is the grid's own:
`WorldSampler.ClassifyCollider` decides what belongs to the map rather than to `dynamics`, and
`staticHazards` reports a box exactly when that classification is a kill volume, so nothing can be
lethal on the grid and absent from the boxes. The two differ only in extent, and only the
pessimistic way: the grid rounds each volume up to whole cells, and a physics query counts a
collider within its contact tolerance (0.01 u) of a cell, so a few of Level1's `^`/`X` cells sit
just outside the exact box that marked them.

### `terrain` — the exact geometry of the ground

The same answer, for the ground itself. For hazards the grid is merely pessimistic; for terrain
it is *wrong in the unsafe direction*, because a vertical face lying on a cell boundary marks the
empty cell on the other side of it too (see "walls live on the cell boundaries" below). Every
ledge therefore ends in a half-cell lip of `#` hanging over the drop, and a client that plans a
landing on that lip falls. Do not plan a landing against `#`; plan it against `terrain`.

```json
"terrain":[{"id":"Ground#0","kind":"solid",
            "paths":[[[-30,-9.21],[-17,-9.21],[-17,-13.21],[-30,-13.21]], …]}]
```

One entry per terrain collider. `kind` is `solid`, `one_way` (a `PlatformEffector2D`) or
`falling` (a `FallingPlatform` at its authored position — the same thing the grid draws as `p`).
`paths` are **closed** polygons in world units: the last point joins the first, and the winding
is normalised so that **the solid side is always to the left of each directed edge** —
counter-clockwise outlines, clockwise holes. That is what lets the client tell a floor from a
ceiling without guessing: a horizontal edge running −x has solid below it, one running +x has
solid above it, and a vertical edge is a wall either way. Unity does not promise which way round
it hands out composite outlines, so `WorldSampler` takes the sign of the total signed area and
flips the whole set if it has to.

A `CompositeCollider2D` reports its real outline through `pathCount`/`GetPath`, which for a
tilemap is the exact boundary of the filled region — the same edges the physics collides with.
Every other terrain collider is reported as its bounding box, as a four-point path: exact for the
axis-aligned boxes this game uses, and an over-estimate for anything rotated or round, which is
the direction the grid already errs in. Level1's whole terrain is 148 points in 18 loops, 1.8 KB.

It is bounded: an outline over `WorldSampler.MaxTerrainPoints` (12000) points is not reported at
all, and `terrainOmitted` says so instead. Reporting a subset would be worse than reporting
nothing — missing terrain reads as open air and a client would plan straight through it —
whereas no terrain at all just puts the client back on the grid, which is where it started. A
client that does not understand `terrain` is on the grid too, so this is additive.

### State

```json
{"id":3,"cmd":"get_state","localWidth":24,"localHeight":12}
```
```json
{"id":3,"result":{
  "tick":184,
  "player":{"alive":true,"pos":[12.4213,-3.1],"vel":[8,-4.2],"aabb":[0.31,0.45],
            "grounded":false,"wallDetected":false,"canDoubleJump":true,"facing":1,
            "atCheckpoint":null,"fruitsCollected":3},
  "dynamics":[
    {"objId":"saw_2","kind":"hazard","pos":[15.5,-2.75],"vel":[0,3],"aabb":[0.55,0.55]},
    {"objId":"pig_1","kind":"enemy","pos":[21.2,-4.1],"vel":[-2,0],"aabb":[0.4,0.35]}],
  "localGrid":{"centeredOn":[12.4213,-3.1],"cellSize":0.5,"width":24,"height":12,
               "rows":["........................",
                       "..........*.............",
                       ".....@..................",
                       "####################.###"]}}}
```
(rows elided to four; the real response has `localHeight` of them.)

`pos` and `vel` are physics truth, never the interpolated render transform — `SimPosition`
reads `Rigidbody2D.position` where there is a body, and rigidbody interpolation is switched off
on the spawned player for exactly this reason. Objects that move by transform (saws, bullets)
have no body velocity, so `SampleDerivedVelocity` reports their per-tick position delta, which
is what an observer could actually measure. `aabb` is half-extents. A dead player reports
`alive: false` and zeroed fields.

`dynamics` covers live objects whose `Kind` is not `None` or `Player`; `radius` filters by
distance from the player. Checkpoints, finish points and start flags are deliberately
`SimKind.None` — they live in the map, not in the per-tick list.

### Plans, observes and terminals

A plan trace has one row per tick plus a row at `startTick` recorded before any input, so a
plan of *n* ticks yields *n + 1* rows:

```json
{"id":9,"cmd":"run_plan","plan":[{"keys":["right"],"ticks":18},
                                 {"keys":["right","jump"],"ticks":1},
                                 {"keys":["right"],"ticks":24}]}
```
```json
{"id":9,"result":{
  "startTick":0,"endTick":43,"ticksRun":43,
  "terminal":"died","deathCause":"hazard:saw_2","checkpoint":null,
  "trace":[{"tick":0,"pos":[2.5,-6.25],"vel":[0,0],"grounded":true},
           {"tick":1,"pos":[2.6333,-6.25],"vel":[8,0],"grounded":true}],
  "events":[{"tick":43,"type":"death","detail":"hazard:saw_2"}],
  "finalState":{"tick":43,"player":{"alive":false,"pos":[0,0],"vel":[0,0],
                                    "grounded":false,"facing":1,"atCheckpoint":null},
                "dynamics":[],"localGrid":{}}}}
```
(the trace is elided to two of its 44 rows.)

An observe trace is the compact form — `{"tick":…, "dynamics":[{"objId":…,"pos":[x,y]}]}` per
tick — because the point of an observe is to watch what moves, not to re-derive the player.

| `terminal` | Meaning |
|---|---|
| `completed` | the plan ran to the end with nothing terminal happening |
| `died` | the player died; `deathCause` carries why |
| `level_end` | the finish was reached (death wins if both happen) |
| `checkpoint` | the plan finished having touched a checkpoint; `checkpoint` carries its id |
| `aborted` | cut short by `abort` or `reset`; on `abort` the partial trace comes back in that command's `job` field |

Terminal is only reported for `run_plan`. A death or a level end stops the run on the tick it
happens; a checkpoint does not stop anything, it just wins the label over `completed` if the
plan finishes without dying. The trace up to a death is kept and returned — it is the client's
main learning signal, so it must never be thrown away.

`events` is the full list, in tick order, of everything the game reported: types are `death`,
`checkpoint`, `level_end`, `fruit_collected`, `enemy_killed`. The names are snake_case, the same
spelling the terminals use, and they come from an explicit map in `SimEvents.WireName` rather
than from the enum member names — renaming a member must not change the wire format.

Death causes, verbatim from the script that killed the player:

| Cause | Source |
|---|---|
| `hazard:<object name>` | `DamageTrigger` — spikes and the like, named after the scene object |
| `hazard:<simId>` | `PlantBullet`, e.g. `hazard:bullet@3` |
| `enemy:<simId>` | `AngryPig`, `Bat`, `Plant` — body contact, e.g. `enemy:pig_1` |
| `fell_out_of_bounds` | `DeadZone`, `SoftRespawnZone` |
| `unknown` | anything that calls `Player.Die()` without a cause — nothing in the project does |

`observe` adds a `warning` when the world is not quiescent — the player is dead, airborne, or
moving — because in that case observing does not just advance the hazards, it advances the
player too.

### Object ids

Ids are assigned by `SimRegistry` and are stable across resets *and* across sessions. Scene
objects are sorted by type name, then hierarchy path, then position — never by instance id,
which varies run to run — and numbered per type: `saw_1`, `pig_2`, `platform_1`. Objects
spawned at runtime get `<type>@<n>` from a counter that `Reset` zeroes, so a replay hands out
the same ids again. The player is always `player` and always sorts first.

## What changed in the existing gameplay code

Thirteen scripts now derive from `SimBehaviour` instead of `MonoBehaviour`: `Player`,
`GameManager`, `Trap_Saw`, `AngryPig`, `Bat`, `Plant`, `PlantBullet`, `FallingPlatform`,
`Trampoline`, `Fruit`, `Checkpoint`, `FinishPoint`, `StartPoint`.

**`Update()` became `SimTick()`.** The body of each `Update` moved verbatim into
`protected override void SimTick()`. `SimBehaviour.Update` calls it in normal play and returns
early in agent mode, where `SimRegistry.TickAll()` calls it instead — in a stable sorted order,
because Unity's inter-object update order is unspecified and shifts as objects are created and
destroyed, which would be enough on its own to make replays drift.

> **If you add a subclass that needs `OnEnable`, `OnDisable` or `Update`, override the
> protected virtual and call `base`.** Declaring your own `private void OnEnable()` hides the
> base method, Unity calls yours instead, `SimRegistry.Register` never runs, and the object
> silently stops ticking and stops appearing in `dynamics` — with no error anywhere.
> `Player.OnDisable` is the pattern to copy.

**Coroutine waits are sim-time.** `yield return new WaitForSeconds(x)` became
`yield return SimClock.Wait(x)`, and `WaitForFixedUpdate` became `SimClock.WaitStep()` —
the latter matters because `WaitForFixedUpdate` keeps running off the engine clock even when
the simulation is frozen. Affected: `Player.KnockbackRoutine` / `WallJumpRoutine`,
`Trap_Saw.StopMovement`, `AngryPig.IdleAtWaypoint` / `DespawnAfterHit`, `Bat.AttackSequence` /
`FlyAlongArc`, `FallingPlatform.FallAndRespawn`, `GameManager.RespawnCoroutine`. Anything that
integrated `Time.deltaTime` now integrates `SimClock.DeltaTime`, and anything that compared
against `Time.time` now uses `SimClock.Time`.

**Snapshot hooks.** `CaptureExtra()` / `RestoreExtra()` carry the script-private state a
transform-and-rigidbody restore cannot see: `Trap_Saw` (waypoint index, direction, `canMove`),
`AngryPig` (waypoint index, state machine, `isDead`), `Bat` (`nextDiveTime`, state, perch),
`Plant` (`nextAttackTime`), `FallingPlatform` (`triggered`), `Trampoline` (`lastBounceTime`),
`Checkpoint` (`isActive`), `FinishPoint` (`isTriggered`). The absolute-time ones are the subtle
cases: sim time restarts at zero on every reset, so a cooldown stored as "the sim time at which
this is allowed again" *must* be restored, or the trampoline would refuse to bounce and the bat
would dive on the wrong tick. `Restore` also stops all coroutines on the object, so a fall
timer cannot survive a reset.

**Despawn instead of destroy.** `Fruit`, `AngryPig` and `Bat` call `SimObjects.Despawn` rather
than `Destroy`. In agent mode an object the scene shipped with is only deactivated, so a reset
can bring it back exactly as it was; anything spawned at runtime is destroyed as usual. Without
this a collected fruit would be gone for the rest of the session and no reset could reproduce
the run.

**Deaths report a cause.** `Player.Die(string cause)` calls `SimEvents.ReportDeath(cause)`
before tearing the player down, and the callers pass something the client can act on. The
server records the tick and the cause; it does not decide what either means.

**The player's control handover is immediate in agent mode.** Normally `RespawnFinished(true)`
arrives from an animation event, which is frame-timed and therefore poison. `Player.Start` now
calls `RespawnFinished(SimClock.ManualMode)`, and `ResetController.SpawnPlayer` calls it
directly, disables rigidbody interpolation (the controller raycasts from `transform.position`,
which under interpolation is a render-time estimate between physics steps), then
`Physics2D.SyncTransforms()` + `RefreshCollisionState()` so the ground and wall flags reported
at tick 0 are real rather than "just constructed". `Player.Awake` also reads
`defaultGravityScale` from the prefab in `Awake` rather than `Start`, because the server spawns
and hands over control in the same frame — before `Start` runs.

**Other frame-timed behaviour that had to move onto the tick.** `Plant` fires from `SimTick`
in agent mode instead of from its animation event (with a `firedOnTick` guard so the event
cannot double the shot); `PlantBullet` ages in sim time instead of relying on a timed
`Destroy`; `Fruit` uses its own type instead of a random sprite index so two runs look
identical; `FinishPoint` skips the level-complete popup (which pauses the game and waits on
animation length) and lets the `level_end` terminal be the whole story;
`GameManager.RespawnPlayer` is a no-op in agent mode, because the server owns respawning and
does it at a known tick with the rest of the world restored to match.

## Known limits and gotchas

**The occupancy grid is an approximation; the hazard boxes and the terrain outlines are not.**
The grid rounds geometry up to whole 0.5 u cells. That is why `staticHazards` reports exact
AABBs and `terrain` exact outlines separately — a jump that really does clear the spikes must
not read as a death, and a ledge that really does end must not read as somewhere to land. Do not
plan against `^`, `X` or `#` cells; plan against the boxes and the outlines. The grid is what the
LLM reads; it is not what a landing is planned against. Both views cover the same thing — the
level as authored — and neither says anything about where a saw is: that is `dynamics`, per tick.

**Walls live on the cell boundaries, so the probe has to cover the whole cell.** A tilemap's
`CompositeCollider2D` with `Outlines` geometry is the *outline* of the filled region — thin
segments, not filled cells — and the tile lattice puts every vertical face on a whole unit.
Cell boundaries are multiples of the cell size from an origin that is itself a multiple of it
(Level1: origin `(-100.0, -22.5)` at 0.5 u), so a whole unit is always one. Level1's horizontal faces sit
mid-cell (`y = n − 0.21`), its vertical ones do not. A probe at 90 % of the cell therefore
sampled every floor and no wall at all: the map showed Level1 with 1781 solid cells and not one
of its walls, so a client could plan straight through an 8 u shaft. The full-cell probe finds
3332. Two consequences follow:

- **The interior of a filled block is still empty on the map.** Only the outline has colliders,
  so a solid block reads as a hollow rectangle of `#`. That is harmless — the inside is
  unreachable — but do not read `.` as "reachable space".
- **A face lying exactly on a cell boundary marks the cell on both sides of it.** A wall reads
  0.5 u thicker than it is and a shaft 0.5 u narrower at each side; the ledge whose face it is
  also gains a 0.5 u lip of `#` hanging over the drop, and the empty column beside a wall reads
  as a ceiling all the way up it. It errs the safe way for walls and the unsafe way for both of
  those, which is exactly why `terrain` exists. Heights are unaffected — a horizontal face inside
  a cell marks that cell only — so it is only ever the *extent* of a surface that the grid gets
  wrong, never its level. Measured on Level1: the grid offered a standing spot 0.62 u out past
  the real edge of the ledge at `x = -17.0`, and a jump rising through the empty cell beside the
  wall at `x = -33.0` was predicted to stop 3.76 u from where the game really put the player.

**The static map is authored geometry, sampled once.** It is re-sampled only when
`get_static_map` asks for a different cell size — never because the world changed shape, and
that is on purpose: re-sampling part-way through a plan would bake a falling platform in
wherever it happened to be on that tick, which is neither the authored layout nor a fact that
survives the next reset. So a falling platform stays on the map as `p` at its starting cell
after it has fallen. Its live position is in `dynamics`, which is the authority.

**Enemy motion is only predictable while the enemy is undisturbed.** `AngryPig` chases on
proximity plus line of sight; `Bat` dives when the player enters its detection box. An observe
trace describes what those objects do *given where the player was standing during the observe* —
it is not a schedule. Predictions from an observe hold for patrol/idle behaviour and stop
holding the moment the player enters detection range.

**A long observe can get the player killed.** Observing advances the world with no input, so a
patrolling pig can simply walk into a standing player. `QuiescenceWarning` catches the obvious
cases (dead, airborne, still moving) but cannot warn about this one. The death shows up in the
observe's `events`; observe never reports a `terminal`, so a client that only inspects
`terminal` will miss it.

**One client at a time.** The listen thread accepts a connection and then serves it inline, so
a second connection sits in the OS backlog until the first disconnects. Queued outbound lines
are dropped if the socket goes away mid-write. Reconnecting is cheap, but reconnecting does not
reset anything — the world is wherever the previous client left it, so send a `reset` first.

**Reset restores what `SimBehaviour` snapshots, plus the `GameManager` progress fields**
(`score`, `fruitsCollected`, `fruitsCollectedByType`, the level timer). Anything else a script
mutated permanently survives a reset. If you add persistent state to a gameplay script, either
put it behind `CaptureExtra`/`RestoreExtra`, or add it to the snapshot in
`ResetController.CaptureWorld` and to `GameManager.AgentRestoreProgress` if it lives on the
manager, or accept that it accumulates across resets.

**Animators, audio and VFX still run per frame.** They are outside the sim and deliberately so;
they must never feed back into gameplay. Anything driven by an animation event is not
replay-safe — that is the whole reason `Plant.Fire` is called from the tick and the player's
control handover no longer waits on one. If you wire up a new animation event that changes
gameplay state, give it a tick-driven path in agent mode.

**`Physics2D.simulationMode` is global.** It is set to `Script` in `Awake` and restored to
`FixedUpdate` in `OnDestroy`. If the server's GameObject is torn down without `OnDestroy`
running, physics stays frozen for the rest of the session.
