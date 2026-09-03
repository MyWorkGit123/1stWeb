# packages/ — shared code as local UPM packages

Each folder here is a **local Unity package** *and* a plain **.NET class library**. The same source
files are compiled by Unity (via `*.asmdef`) and by `dotnet build` (via `*.csproj`), so there is one
copy of the code and no DLL-copy step.

| Package | Unity dependency? | Purpose |
|---|---|---|
| `com.brinehold.core` | **No** | `Fix64` fixed-point maths, deterministic PRNG, dense collections, bit serialisation |
| `com.brinehold.sim` | **No** | The authoritative simulation: world state, systems, rules, pathfinding, snapshots |
| `com.brinehold.content` | **No** | Content schema, loader and the authored JSON game data |
| `com.brinehold.protocol` | **No** | Wire messages and source-generated codecs |
| `com.brinehold.net` | **No** | Transport abstraction, channels, replication, interest management |
| `com.brinehold.ai` | **No** | Server-side AI players; emits ordinary commands |

**Hard rule:** nothing in `packages/` may reference `UnityEngine`. See
`TECHNICAL_ARCHITECTURE.md` §1 and §10.

*Status: scaffold only — no code yet (M0).*
