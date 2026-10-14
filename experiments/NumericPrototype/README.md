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
