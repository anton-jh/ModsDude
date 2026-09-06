using Microsoft.Extensions.Logging;

namespace ModsDude.Client.Core.Persistence;

public class StateStore(ILogger<StateStore> logger)
    : Store<LocalState>("state.json", state => state.Version == LocalState.CurrentVersion, logger);
