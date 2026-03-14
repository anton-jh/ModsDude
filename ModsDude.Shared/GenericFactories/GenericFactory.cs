namespace ModsDude.Shared.GenericFactories;

public class GenericFactory<T>(Func<T> factory) : IFactory<T>
{
    public T Create()
    {
        return factory();
    }
}
