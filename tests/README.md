# tests/ — non-code test assets

| Folder | Contents |
|---|---|
| `replays/` | The golden replay corpus. Every replay here is re-simulated on three platforms on every PR and must produce identical state hashes (`TESTING.md` §6) |
| `maps/` | Tiny deterministic maps used by simulation scenario tests |
| `fixtures/` | Hand-built world states for scenario tests |

Every determinism bug ever found gets a permanent replay in `replays/`.

*Status: scaffold only (M0).*
