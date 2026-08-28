namespace ModsDude.Client.Core.Persistence;

public class StateStore() : Store<LocalState>("state.json", state => state.Version == LocalState.CurrentVersion);
