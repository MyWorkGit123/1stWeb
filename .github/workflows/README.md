# CI workflows

| Workflow | Trigger | Gate |
|---|---|---|
| `ci.yml` | every PR | Unit, simulation and networked-integration tests; analysers; content validation. Target < 5 min. **Blocking** |
| `determinism.yml` | every PR + nightly | Replay corpus across ubuntu-x64 / windows-x64 / macos-arm64. **Blocking** |
| `unity-build.yml` | main + tags | Client and server artifacts |
| `loadtest.yml` | nightly | Performance budgets |
| `fuzz.yml` | nightly | Protocol fuzzing |

See `TESTING.md` §13.

*Status: not yet implemented — workflows land in M1 (`ci.yml`) and M4 (`determinism.yml`).*
