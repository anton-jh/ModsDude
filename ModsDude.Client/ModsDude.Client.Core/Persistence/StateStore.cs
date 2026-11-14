using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Persistence;

public class StateStore(ILogger<StateStore> logger)
    : Store<LocalState>(FileSystemHelper.GetAppDataDirectory(), "state.json", LocalState.Schema, logger), IStateStore;
