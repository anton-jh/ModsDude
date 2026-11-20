# Roadmap

What is planned, roughly in order of how settled it is. Built work moves to its area page; this page
only holds what doesn't exist yet.

## Planned

**Savegames for more games.** Minecraft, Stationeers and Space Engineers, as savegame-only adapters.
The savegame flows already handle adapters without mods: no profile, no compatibility mode, no mod
targets. The repo page still always shows its profile list, which such a game wouldn't need.

**Load order and dependencies between mods.** Not modelled at all yet. It will need its own design:
where the data comes from (adapter or user), how it shows in the editor, and how it affects apply.

**Paging the profile history.** See [Known issues](known-issues.md#unfinished).

## Open ideas

- **Offering a check-out when activating a profile** that has a savegame nobody holds.
- **A contact address in the ModHub crawler's User-Agent**, once there is an address to give.

## Not planned

- **A web client.** The only web UI is the server's admin pages.
- **Public sign-up.** Signing up needs a trust code from the operator or a repo invite.
- **Multi-tenancy, quotas and abuse handling** beyond blocking a user.
