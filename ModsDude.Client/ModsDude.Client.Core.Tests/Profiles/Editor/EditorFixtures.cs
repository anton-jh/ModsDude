using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.Services;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.Profiles.Editor;

internal static class EditorFixtures
{
    public static readonly ModSource Downloads = new(ModSourceId.Downloads, "Downloads", @"C:\Downloads", ModSourceKind.Downloads);
    public static readonly ModSource Usb = new(ModSourceId.ForFolder(@"E:\mods"), "mods", @"E:\mods", ModSourceKind.AdHoc);
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);


    public static ProfileModPin Pin(string modId, string version, bool locked = false)
        => new(Mod(modId), V(version), new ProfileModLock(false, locked));

    public static CatalogModVersion Registered(string modId, string version, int sequence, bool locked = false, DateTimeOffset? registered = null)
        => new(Mod(modId), V(version), modId, "", IsLocal: false, IsOnServer: true, Locked: locked)
        {
            SequenceNumber = sequence,
            Registered = registered ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(sequence)
        };

    public static CatalogModVersion Local(string modId, string version, ModSource? source = null)
        => new(Mod(modId), V(version), modId, "", IsLocal: true, IsOnServer: false, Locked: false)
        {
            FoundIn = [new ModOccurrence(source ?? Downloads, $@"{(source ?? Downloads).Path}\{modId}.zip", 100, () => Stream.Null)]
        };

    public static ProfileEditorCatalog Catalog(IEnumerable<CatalogModVersion> visible, IEnumerable<CatalogModVersion>? standby = null)
    {
        var shown = visible.ToList();

        return new ProfileEditorCatalog(
            new ModCatalogSnapshot(shown, [.. shown, .. standby ?? []], []),
            DefaultModVersionComparer.Instance);
    }

    public static ProfileEditorInputs Inputs(ProfileDraft draft, params CatalogModVersion[] visible)
        => new(draft, Catalog(visible), Now);

    public static ProfileDraft Draft(params ProfileModPin[] saved) => new(saved, []);

    public static ProfileEditorState Compute(ProfileEditorInputs inputs) => ProfileEditorState.Compute(inputs);

    public static PinnedModRow Row(this ProfileEditorState state, string modId)
        => state.Pinned.Single(x => x.ModId == Mod(modId));

    public static IEnumerable<string> Shown(this IEnumerable<PinnedModRow> rows) => rows.Select(x => x.ModId.Value);

    public static IEnumerable<string> Shown(this IEnumerable<AvailableModRow> rows) => rows.Select(x => x.ModId.Value);
}
