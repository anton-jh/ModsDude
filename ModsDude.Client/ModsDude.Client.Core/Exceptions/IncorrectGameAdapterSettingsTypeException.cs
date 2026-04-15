namespace ModsDude.Client.Core.Exceptions;

public class IncorrectGameAdapterSettingsTypeException<TExpected>(object settings)
    : Exception($"Incorrect game adapter settings type. Expected '{typeof(TExpected).FullName}', got '{settings.GetType().FullName}'");
