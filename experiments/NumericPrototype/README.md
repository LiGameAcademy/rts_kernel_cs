# Numeric contract probe — Issue #68, step 1

This is an isolated experiment, not a second supported RTS simulation. Production spatial types and v9 snapshots still use double. Nothing references this experiment from Rts.Kernel or the Godot host.

Run `./experiments/NumericPrototype/Test-Prototype.ps1` with the existing .NET 10 SDK. The runner builds Debug/Release and compares raw math traces from two independent processes in each configuration. This proves only the tested Windows machine/configurations; it is not a cross-CPU or cross-platform certification.

## Contract findings

- FixPointCS source is unmodified, pinned to `a852f05b428a942f8dc274ee516a893ae224e0d4`. `vendor/FixPointCS/SOURCE.json` records Git blob and SHA256 identities. MIT license is included. Its local SDK project is ours; the three numeric sources are third-party files and intentionally retain upstream layout.
- Candidate multiplication floors negative fractions. The proposed project contract truncates toward zero. `PrototypeScalar` uses checked Int128 intermediate arithmetic for this probe; candidate integer square root and trigonometric functions are tested separately. This is evidence that raw vendor API calls cannot silently define project semantics, not approval of the wrapper as a production API.
- Checked compilation reproduces an OverflowException in the original SplitMix64 implementation. The core fix explicitly declares its existing modulo-2^64 operations unchecked. Known-answer vectors and restored-state continuation are tested by compiling the actual source into the checked probe. Normal builds keep the same sequence.
- Add/multiply/divide overflow, zero divisor, negative root, negative grid flooring and lossless .NET raw JSON round-trip are exercised. Decimal command ingestion/quantization is NOT implemented yet. JSON behavior in a .NET reader says nothing about JavaScript precision.
- Floating-point values only serve as sample error oracles in this probe. They are not fed into raw math traces. The sine/atan2 samples are limited to angles [-1,1] and ratios [-10,10]; they do not establish a global error bound. Square-root checks include quantization of the squared input.

## Results and remaining scope

2,118 checks per process. Initial raw trace SHA256: `9f4c05d906c21313a8e2d615b87b1a0a54140d84a56792caece87ec1db938975`.

Remaining Issue #68 work: real content range survey, input quantization, a genuine terrain/pathfinding/turning/slope/collision chain, in-progress matching snapshots, actual Godot execution, 300/500 business/performance measurements and numerical range proof. No claim that an arithmetic loop satisfies those gates. This step does not authorize switching the host, changing v9, replacing the published v0.0.1 tag, or closing #68.

## Step 2: exact decimal input and real content survey

`InputQuantizer` parses JSON-number text using exact BigInteger rationals, without a double/decimal conversion in the authority path. It rounds to nearest raw unit, ties away from zero, and rejects any exact input outside the representable interval before rounding. JSON grammar, a 256-character token bound and an exponent bound of +/-256 are explicit experimental ingestion limits. This is offline conversion code, not a hot-loop numeric type.

`dotnet run --project experiments/NumericPrototype -- --survey <assets-directory>` reads existing UnitData/UnitBalance exports and parsed Echo Isles units/pathing/heights. It records source SHA256, per-field range, skipped non-number/missing fields and decimal diagnostic quantization error. Numeric overflow aborts rather than disappearing from statistics. Source content is not bundled. The survey reflects the existing exporter output, not the original binary map or every supported map/mod.

4,206 checks per probe process; Debug/Release each run two independent processes. Existing raw math hash is unchanged. New decimal-input trace is `6d8de2d1136af0129063576981e9caa3f3cb1c68398d8ca07e8cc959dbe95621`. Boundary cases include exact half units, signed rounding, values just outside representable bounds, exponents, cultures, dyadic fractions, missing values, source identity and survey overflow.

Real local samples: pathing512x384, cell32; heights12,513 values226.25..722.5; numeric unit speed617 records0..405 (219 non-numeric), collision836 records8..196. These are observed samples, not approved business limits or interpreted movement definitions. Position range alone does not prove squared-distance or matching-aggregate intermediate safety. Wide intermediate/cost representation remains a separate design decision; this step does not implement a replacement matching algorithm or a genuine migrated movement chain.

## Step 3: wide squared-distance/cumulative cost

The user accepted separating ordinary 64-bit fixed-point state from wider intermediate/cost types. WideSquaredDistance uses checked Int128 in coordinate-raw squared units (2^64 scale), without prematurely reducing to a 32.32 scalar. A conservative Echo Isles bounding-box diagonal square is 419,430,400 world-unit squared; 500 such costs total 209,715,200,000. The latter cannot fit a 32.32 scalar but fits the tested wide representation. This is a bound calculation, not a measured Hungarian execution or a claim those corner points are traversable.

The probe covers symmetry, fractional products, explicit overflow at unrestricted Int64 coordinate extremes and nonnegative cost round-trip via JSON decimal strings. Int128 is not universally safe for all combinations of full-range coordinates. Matching signed potentials/reduced costs still require a distinct range analysis; this nonnegative type is not a completed matching replacement.

Current probe count: 4,214 per process; math/input/wide-cost hashes are checked across two Debug and two Release processes. No genuine movement, terrain sampling, collision or in-progress matching restoration has been implemented in this batch. No reverse engineering was needed to establish these arithmetic facts; original-game behavior does not determine our overflow or storage policy.

## Step 4: single-unit movement, exact collision oracle and restoration

Run `./experiments/NumericPrototype/Test-Prototype.ps1 -AssetsDirectory <local-assets-directory> -ReportPath <output.json>` to compare Debug/Release, two independent processes per configuration, including the existing parsed Echo Isles map and exported hfoo fields. CI has no original assets and runs self-contained synthetic fixtures only; the optional local probe never copies those assets into the repository.

The isolated chain quantizes the terrain/definition inputs, plans an integer octile path, samples bilinear height, applies current turn/slope rules, checks proposed movement, commits immutable state, emits ordered terminal events and restores detached raw snapshots. The integer path metric uses straight10,000,000/diagonal14,142,135 and stable score/index ordering. This is a numerical experiment, not trajectory equivalence with the production floating-point implementation.

- Time uses 30 logical ticks. Arrival compares exact raw squared distance against available distance; elapsed waypoint fractions consume a ceiling budget. Component advancement uses one wide integer ratio to avoid a second quantization. A goal one raw unit beyond the frame budget cannot arrive early (the initial prototype failed this regression before correction).
- The pinned provider's raw Pi and Pi2 are independently rounded. Angle wrapping uses a unique interval for the actual odd Pi2 period; equivalent periods cannot produce snapshot aliases.
- Position/distance calculations use checked wide integer intermediates. Exact swept-circle reference predicates additionally need higher-degree products, so CollisionOracle uses BigInteger as a deliberately slow reference. This is NOT approval of BigInteger for the production movement hot path; a measured bounded implementation is still required.
- Radius is interpreted according to the current host placement rule, not reverse-engineered original semantics. The real hfoo sample has speed270, collision/radius31, turnRate0.6. Clearance follows ceil(radius/cellSize-0.5), so the probe does not shrink the body to pass narrow routes. The user explicitly chose to validate the current kernel turns-per-second contract first; original UnitData.turnRate conversion remains unproven and will be researched separately before formal adoption.
- This probe is bounded to aligned terrain spanning at most16,384 world units per axis, speed up to1,000, radius up to4 pathing cells and turn rate0.05..1 revolutions/second. These are experimental limits, NOT engine business limits. Only static pathing, sampled terrain, one moving circle and an optional stationary circle are modeled. There is no moving-crowd avoidance, dynamic obstacle replan, order queue, AI or group matching here.
- Experimental snapshot format1 is unrelated to production v9. Identity includes source terrain text, selected parameters, start/goal, blocker and a numeric contract identifier. Restore rejects mismatched content/path, invalid counters/status and impossible occupancy before mutating state. Changing numerical rules requires a new experimental contract identifier. Pending output events are not replayed.

4,610 checks per synthetic probe process. Local real map: first eligible candidate route, no failed moving candidates; 12 waypoints, 43 frames to exact goal; adding the stationary blocker fails at frame108 after90 consecutive blocked frames, one terminal event. Both normal movement and restored continuation match raw state/events every frame. Source identities and four-process results are preserved in internal research records; these samples are not whole-map quality or cross-machine proof.

Remaining #68 scope: actual Godot execution, in-progress matching/potential range and restore, bounded production collision strategy, 300/500 business/performance measurements, broader numerical error budget and a final migration decision. Production double/v9 and the host pointer are unchanged.


### Signed incremental matching prototype (2026-10-14)

`Matching/WideMatchingWork.cs` validates the current Hungarian matching phases with
signed `Int128` potentials/reduced costs in coordinate-raw squared units (2^64).
It preserves row/column tie order, zero-edge seeding and suspension between whole
column batches. `null` represents an unvisited minimum; no numeric infinity sentinel
participates in arithmetic. This isolated square matrix excludes the production
anchor/cancellation/task scheduler and does not change production matching or v9.

Experimental bounds are 1–500 matched rows and each cost in [0,C], where
C = 2 × (16384 × 2^32)^2. This is an explicit conservative matrix contract, not
permission to subtract arbitrary Int64 coordinate extremes. Coordinate input must
still satisfy the separate distance/world bounds before producing these costs.

With feasible seeded duals, initial row/column potentials lie in [0,C]. Each batch
has nonnegative delta; its update raises the dual objective by delta (the used tree
has one more row than real column). A perfect assignment bounds this objective by
nC, so cumulative deltas are at most nC. Thus row potentials stay in [0,(n+1)C],
column potentials in [-nC,C], and a conservative signed subtraction envelope
(2n+3)C is below Int128.MaxValue even for n=500. Checked arithmetic remains enabled.
Tests verify dual feasibility and tight assigned edges at suspension boundaries.

Experimental snapshot format 1 writes signed Int128 values as decimal strings,
including minimum entries (null means unset). It owns detached arrays and binds the
cost matrix/order/algorithm identity. Restore reconstructs the same deterministic
batch history and compares every saved field before returning a new owner. This
is a deliberately expensive **validation oracle**, not a production restoration
implementation or 500-member performance claim. Corrupt counters, potentials,
predecessors and content identity are rejected. No stable snapshot format changes.

The default runner checks 84 cases of sizes 1–7 against an independent exhaustive
BigInteger permutation oracle, budgets 1–3 versus uninterrupted execution, JSON
restore at every suspension, negative potentials and stable ties. A separate
500-row maximal equal-cost case checks the bound and deterministic assignment;
it does not measure a difficult 500-row solve. Test-Prototype.ps1 checks the raw
matching trace across two Debug and two Release processes. Remaining work includes
production structural restore design, real group coordinates/cancellation, Godot
execution, and representative 300/500-member business/performance measurements.
