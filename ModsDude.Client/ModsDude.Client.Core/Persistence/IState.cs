namespace ModsDude.Client.Core.Persistence;

public interface IState<T> where T : IState<T>
{
    int Version { get; set; }

    static abstract T Create(int version);
}
