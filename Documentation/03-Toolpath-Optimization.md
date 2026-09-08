# 03 — Toolpath Optimization

This is the document that addresses *"edge cuts can be all over the place causing tons of
excessive travel moves."* That symptom has specific, findable causes in pcb2gcode.

## 1. What pcb2gcode actually does

From `WorkingFolder/pcb2gcode/src/tsp_solver.hpp`:

```cpp
// nearest_neighbour(), line ~100
auto newDistance = distance(currentPoint, get(*i, Side::FRONT));
```

and the distance metric:

```cpp
// line ~63 — Chebyshev distance
return std::max(std::abs(p0.x() - p1.x()), std::abs(p0.y() - p1.y()));
```

Then `tsp_2opt()` runs 2-opt, where a "move" reverses a contiguous run of paths.

### The four defects

1. **Greedy only ever measures the FRONT endpoint of each candidate path.**
   `get(*i, Side::FRONT)` — the loop never asks *"is this path's other end closer?"* A path whose
   tail is 1 mm away but whose head is 80 mm away is treated as 80 mm away. Reversal is only
   recovered later, by 2-opt, and only if reversing an entire contiguous *run* happens to help.
   This alone produces the zig-zag pattern the user is seeing.

2. **Closed loops are pinned to an arbitrary start vertex.**
   Edge cuts, outlines, and pocket contours are closed. A closed loop can be entered at *any* of
   its vertices at zero cost. pcb2gcode always enters at whatever vertex happened to be first in
   the data structure. On a board with eight cutouts this throws away most of the available
   optimisation before the solver even starts.

3. **Chebyshev distance is not the cost.**
   The comment says it approximates rapid time — it does, for the XY move on a machine that moves
   both axes at full speed. But it ignores the dominant terms: the **Z lift**, the **plunge**,
   the **dwell for spindle state changes**, and **acceleration**. On a PCB job with 400
   short isolation paths, the lift+plunge pairs are most of the wall-clock time, and the optimizer
   is blind to them.

4. **2-opt with segment reversal is a weak neighbourhood and it is O(n²) per pass.**
   The `while(found_one)` loop re-scans from scratch after every improvement. On a few thousand
   paths this is slow enough that in practice people turn optimisation down, which makes the
   output worse, which is how you end up with "edge cuts all over the place".

There is no precedence handling either: nothing stops the outline being cut before the holes are
drilled, which on a real machine means drilling a board that is no longer held down.

## 2. The correct model: Generalized TSP with entry sets

Each toolpath is a **node**. Each node has a **set of allowed entry/exit configurations**:

| Path kind | Configurations |
|---|---|
| Open polyline | 2 — enter at head (exit tail), or enter at tail (exit head) |
| Closed loop | 2·V — any vertex, either direction. In practice sample a bounded subset (see §4). |
| Drill hit | 1 — it is a point |
| Path with a mandatory lead-in | 1 or 2 depending on lead geometry |

This is a **Generalized TSP** (choose one configuration per node, then order the nodes). It is the
right formulation and it is where the big wins live: pcb2gcode is solving a much smaller,
artificially constrained version of the problem.

## 3. The cost model

Replace Chebyshev with an actual **time** estimate for the transition from configuration `a` of
path `i` to configuration `b` of path `j`:

```
cost(i.a → j.b) =
      t_retract(depth_i → z_safe)          # Z lift at plunge/retract feed
    + t_rapid(exit_i → entry_j)            # XY rapid, trapezoidal profile
    + t_plunge(z_safe → depth_j)           # or ramp/helix time if ramping
    + t_state_change                       # spindle change, tool change, dwell
```

`t_rapid` uses a **trapezoidal motion profile** with the machine's real acceleration and max
feed, not a distance:

```
t(d) = (d ≥ d_crit) ? d/v_max + v_max/a          # reaches cruise
                    : 2·sqrt(d/a)                # triangular, never reaches v_max
```

This matters enormously: on a machine with 500 mm/s² acceleration, 200 short moves cost far more
than their summed distance suggests, and the optimizer should prefer *fewer, longer* moves. It
also means the number the UI shows as "estimated time" is actually right, which nothing in this
space gets right.

The cost model is **mill-only**, and that is a simplification the SVG-for-lasers decision buys
us ([04 §1](04-Machines-Laser-and-Mixed-Workflows.md#1-two-machines-two-output-formats)): laser
ordering, overscan and scan strategy belong to the laser software, so the optimizer has exactly
one machine model to be right about.

## 4. The algorithm

```
1.  Build candidate lists.
    STRtree over all entry points. For each node keep the k=10 nearest other nodes'
    configurations. Reduces the neighbourhood from O(n²) to O(nk).

2.  Reduce closed-loop configurations.
    A 500-vertex loop does not need 1000 configurations. Sample entry vertices by
    keeping every vertex that is a local minimum of distance to any of the k nearest
    neighbours' entry points, capped at 16 per loop. Near-optimal, bounded cost.

3.  Constrained greedy construction.
    Nearest-neighbour over *configurations*, not paths — so entering a path backwards is
    considered from the start, fixing defect #1. Respects precedence (§5).

4.  Local search, until the time budget expires:
      · 2-opt          — reverse a run  (what pcb2gcode has)
      · Or-opt         — relocate a run of 1–3 nodes elsewhere  (it does not have this;
                         this is the move that fixes "one stray edge cut in the middle")
      · configuration flip — re-choose a node's entry point/direction in place
      · 2h-opt / or-3opt  — combined relocate+reverse
    Use don't-look bits and a delta-evaluated objective so each move is O(1), not O(n).

5.  Optional restarts.
    Deterministic seeded perturbation (double-bridge kick) + re-optimise, keep the best.
    This is a bounded Lin–Kernighan-style improvement without implementing full LKH.
```

Complexity is ~O(n·k) per local-search sweep. For n = 5000 paths this runs in well under a second,
which is the whole point — it can run inside the live pipeline.

## 5. Precedence constraints

The optimizer takes a partial order and never violates it. Constraints come from physics, not
preferences:

| Constraint | Reason |
|---|---|
| All drilling before the outline cut | The board must still be held down when you drill it |
| Inner cutouts before the outer boundary | Same |
| Isolation before mask/pour removal | Avoid re-cutting cleared area |
| Deeper passes after shallower on the same contour | Chip load |
| Tool-group contiguity (all paths for tool T together) | Tool changes are expensive and error-prone |
| Same-setup contiguity | Never interleave operations from different machines |
| Fiducials first, in every setup | They must exist before anything references them |

Implement as a DAG; the greedy construction only picks from the ready-set, and local-search moves
are rejected if they violate the order. Tool grouping is handled as a **hard partition**: solve
each tool group as its own GTSP, then order the groups (there are only a handful).

## 6. Time-boxing

The optimizer gets a **budget**, exposed in the UI as a three-position control:

| Mode | Budget | Typical use |
|---|---|---|
| Fast | 50 ms | Live preview while dragging a slider |
| Balanced | 500 ms | Default; runs on every settled edit |
| Thorough | 10 s | One click before exporting the final file |

Same seed, same result, every time. The UI shows the improvement so the value is visible:

> **Travel: 412 mm → 96 mm (−77%)   ·   Est. time 14:20 → 8:05   ·   Lifts 318 → 291**

Being able to *see* that number move is what turns "the optimizer is better" from a claim into a
fact the user can check.

## 7. Beyond ordering

Ordering is necessary but not sufficient. Three more sources of wasted time:

1. **Excessive lifts.** If two consecutive paths' endpoints are closer than a threshold and the
   straight line between them stays outside the keep-out geometry, **do not lift at all** —
   travel at cutting depth. pcb2gcode has `backtrack.cpp`/`path_finding.cpp` doing a limited
   version of this;
   generalise it with an STRtree-based visibility check and a configurable "max distance to
   travel at depth".

2. **Path merging via Eulerian traversal.** Adjacent isolation contours often share endpoints. Build
   the connectivity graph and find Eulerian paths so a chain of segments becomes one continuous
   move with no lift. pcb2gcode does this (`eulerian_paths.cpp`) and it is one of the things it
   gets right — keep the idea, implement it cleanly, and extend it to work *across* the
   containment tree.

3. **Arc fitting and simplification.** Fewer, longer segments means the controller's look-ahead
   planner can actually reach full feed. A pcb2gcode isolation file can be 300k lines of `G01`
   with 1 µm steps; the machine decelerates for every one of them. Douglas–Peucker at 5 µm plus
   G2/G3 fitting typically cuts file size 10–20× and speeds the *actual* cut by 20–40%, which is
   a bigger real-world win than the travel ordering.

## 8. Acceptance criteria

Benchmark against pcb2gcode on the corpus, same board, same tool, same depths:

- **Rapid travel distance:** ≥ 40% reduction on outline/edge-cut operations, ≥ 25% overall.
- **Estimated cut time** (trapezoidal model): ≥ 20% reduction.
- **G-code line count:** ≥ 5× reduction via simplification + arc fitting.
- **Optimizer wall time:** < 500 ms at Balanced for a 5000-path board.
- **Zero** precedence violations, verified by an assertion in the golden tests.

These go in CI as a regression gate. If a change makes travel worse, the build fails.
