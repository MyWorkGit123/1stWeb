# tools/ — developer and CI scripts

| Folder | Contents |
|---|---|
| `build/` | `build-server.sh`, `build-client.sh`, `package.sh` |
| `ci/` | `run-tests.sh`, `determinism-matrix.sh` |
| `dev/` | `run-local-match.sh`, `run-two-clients.sh`, `netsim.sh` |

`tools/dev/run-two-clients.sh` is the one-command way to run the M3 prototype locally: it starts a
headless server in listen mode and launches two client windows against it. See `TESTING.md` §7.

*Status: scaffold only (M0).*
