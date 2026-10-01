using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Tests.GameProcesses;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>Which revision a savegame runs on or declares.</summary>
public class SavegameRevisionRulesTests
{


    /// <summary>
    /// The revision a first snapshot declares: what the folder is actually on where the chosen profile
    /// is the one it is on, and that profile's head otherwise - which is the honest answer, since the
    /// alternative is a number belonging to a different mod list.
    /// </summary>
    [Fact]
    public void The_declared_revision_is_the_folder_s_where_the_profile_matches_and_head_otherwise()
    {
        var profileId = Guid.NewGuid();

        Assert.Equal(4, SavegameRevisionRules.DeclaredRevisionFor(profileId, 1004, profileId, 4));
        Assert.Equal(1004, SavegameRevisionRules.DeclaredRevisionFor(profileId, 1004, Guid.NewGuid(), 4));
        Assert.Equal(1004, SavegameRevisionRules.DeclaredRevisionFor(profileId, 1004, null, null));
    }
}
