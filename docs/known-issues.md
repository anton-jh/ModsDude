# Known issues

What is wrong, unfinished or risky in the code as it is now. Remove an entry in the same change that
fixes it.

## Correctness

**The space check can under-count when an uninstalled file must be moved into the store.**
`ModSyncSpaceNeeds.AddRemoval` skips the space for a recoverable file whenever *any* store holds its
hash. But the executor only deletes such a file when it *is* the serving store's blob, and otherwise
moves it into the serving store. When the serving store is on another disk than the mod folder and the
only copy is in yet another store, that move needs space the check never counted.

**Some removals bypass the Recycle Bin.** A recoverable file whose bytes are in the serving store is
deleted directly, or moved into the store (`ModSyncExecutor.RemoveOneAsync`). The bytes are kept and can
be fetched again, but CLAUDE.md says anything ModsDude deletes from a game folder goes to the Recycle
Bin. Decide which is right and align the code or the rule.

**Attributes are missing on older mod versions.** Versions registered before the adapter reported
attributes have none, and nothing backfills them, so attribute search can't find them. This conflicts
with the rule that changes to stored data ship with a backfill.

**Nothing checks `Generated.cs` against `openapi/v1.json`.** CI checks the document against the API, but
not the generated client against the document. A forgotten `nswag run` only shows up as a runtime
mismatch.

**The ModHub crawl's logic lives in its Hangfire class.** The sweep, poll and refresh steps are in
`ModHubCrawlJob` itself, not in testable code beside it, which CLAUDE.md requires of background jobs.
Only `ModHubListingChanges` has been pulled out.

**Revoking an invite doesn't handle a concurrent change.** `RevokeInviteV1Endpoint` doesn't catch
`DbUpdateConcurrencyException`, so a revoke racing another change to the same invite answers 500 instead
of a typed problem.

## Unfinished

**The repo page always shows profiles.** Its profile list and Create button show even when the adapter
has no mod support, where profiles mean nothing.

**Profile history doesn't page.** The server windows revisions with skip and limit, but the client only
shows that there are more (`ProfileHistoryPageViewModel`).

**The usage list pages by offset.** `GetModUsage` uses an offset cursor, so a page can skip or repeat
rows if usage changes between requests.

## Risks and traps

**A sign-up holds its codes before its email is confirmed.** The invite uses and the trust code are
redeemed at sign-up. An account whose email is never confirmed keeps them anyway.

**A sign-in dropped by a cancelled user switch stays valid.** The client forgets its refresh token, but the
server doesn't hear of it, so the token works until it expires.

**Token certificates are replaced by hand.** They expire after two years, and nothing warns before then.
Replacing them signs every user out.

**Dropping an adapter version strands its repos.** The server stores a repo's adapter ID without
checking it. A client build without that adapter version can't open the repo, and nothing warns about
it beforehand.

**User tags can collide.** `UserTag` is four digits derived from the ID, so two users with the same name
can, rarely, also share a tag.

**Invite codes are stored in plain text.** Only their creator can read them, but anyone with database
access can too. Accepted for now, because codes are revocable and can be limited by time and uses.

**EF owned collections always load.** `ProfileRevision.Pins` and `ModVersion` attributes and images are
owned, so loading the owner loads them all. Query projections, not entities, when only the owner is
needed.

**Value objects order in SQL but don't compare.** EF translates `OrderBy` on a value-object column, but
not `<` or `>` against one. Compare the underlying value, as the mod feed does with its plain `long`
sequence.

## Hygiene

- Some pages read detail data straight from the API instead of a store, so the change poll doesn't
  update it: savegame snapshots and check-outs, mod dependents, the editor's pins and ignore list, and
  archived profiles.
- `GET users` has no caller in the client.
- `ModsDude.Server.Domain/Mods/ModTag.cs` is unused.
- Time types are mixed: `ITimeService` returns `DateTime`, while some entities use `DateTimeOffset`.
- Several long type comments repeat explanations that now live in these docs, for example on
  `ModImportService` and `ProfileModUpdates`.
