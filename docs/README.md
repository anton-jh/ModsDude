# ModsDude documentation

Documentation for the ModsDude system as it exists today, plus the plan for where it is
going. Written for the people working on it — it assumes you can read the code, and
concentrates on the things the code does not tell you.

| Document | What is in it |
| --- | --- |
| [01 — Overview](01-overview.md) | What ModsDude is, the mental model, how the pieces fit together |
| [02 — Domain model](02-domain-model.md) | Entities, identity, invariants, and why they are shaped that way |
| [03 — Server](03-server.md) | Project layout, request pipeline, authorization, persistence, storage, full API reference |
| [04 — Game adapters](04-game-adapters.md) | The three-stage adapter model, dynamic forms, and how to add a game |
| [05 — Client](05-client.md) | WPF architecture, navigation, page lifecycle, local state, mod imagery, page inventory |
| [06 — Flows](06-flows.md) | End-to-end walkthroughs of every flow the system supports today |
| [07 — Mod sync design](07-mod-sync-design.md) | The designed-but-unbuilt core feature: applying a profile to a game install |
| [08 — Known issues](08-known-issues.md) | Bugs, stubs, dead code, and scaling limits found in the current tree |
| [09 — Mod representation and the catalog](09-mod-catalog.md) | Local vs registered mods, the merged model, the import/manage/profile-editor pages, and the import protocol |
| [PLAN](PLAN.md) | Roadmap, phased |

## Conventions used here

- **Documented as reality.** These pages describe what the code does now, including where
  that is wrong. Anything not yet built is marked *Not implemented* and lives in
  [PLAN.md](PLAN.md).
- Code references are paths relative to the repository root.
