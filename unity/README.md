# unity/ — the Unity client (view layer only)

`BrineholdClient` is a Unity 6 LTS project responsible for **rendering, input, UI, audio and camera**
— and nothing else. It references the packages in `../../packages` via `file:` entries in
`Packages/manifest.json`.

The client never decides a game outcome. It sends commands to the authoritative server, and displays
the state the server replicates back. See `MULTIPLAYER_ARCHITECTURE.md` §2.3 and
`TECHNICAL_ARCHITECTURE.md` §8.

*Status: scaffold only — the Unity project has not been created yet (M0).*
