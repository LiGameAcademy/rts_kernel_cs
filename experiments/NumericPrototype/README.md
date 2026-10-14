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
