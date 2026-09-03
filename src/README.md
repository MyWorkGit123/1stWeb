# src/ — .NET executables and test projects

| Project | Purpose |
|---|---|
| `Brinehold.Server` | The headless authoritative match host (.NET 8). One process per match |
| `Brinehold.Tools.ReplayCheck` | Re-simulates a replay and verifies its state hashes |
| `Brinehold.Tools.ContentCheck` | Validates content data, chain closure and balance bounds |
| `Brinehold.Tools.MapCompiler` | Compiles authored maps into deterministic map binaries |
| `Brinehold.Tools.LoadTest` | Drives N headless bot clients against one server |
| `Brinehold.*.Tests` | xUnit suites — run with `dotnet test`, no Unity editor required |

*Status: scaffold only — no code yet (M0).*
