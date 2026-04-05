namespace ModsDude.Client.Core.Helpers;

public readonly struct Maybe<T>
{
    private readonly T _value;


    private Maybe(T value)
    {
        _value = value;
        HasValue = true;
    }


    public T Value => HasValue
        ? _value
        : throw new InvalidOperationException("No value present.");

    public bool HasValue { get; }

    public static Maybe<T> None => default;

    public static Maybe<T> From(T? value) =>
        value is null ? None : new Maybe<T>(value);

    public T GetValueOrDefault(T defaultValue)
    {
        return HasValue ? Value : defaultValue;
    }


    public static implicit operator Maybe<T>(T value) =>
        value is null ? None : From(value);

    public static implicit operator bool(Maybe<T> maybe) =>
        maybe.HasValue;
}

public static class Maybe
{
    public static Maybe<T> From<T>(T? value)
        => Maybe<T>.From(value);
}


public static class MaybeExtensions
{
    extension<T>(Maybe<T> maybe)
    {
        public Maybe<TResult> Select<TResult>(
        Func<T, TResult> selector)
        {
            return maybe.HasValue
                ? selector(maybe.Value)
                : Maybe<TResult>.None;
        }

        public Maybe<TResult> SelectMany<TResult>(
            Func<T, Maybe<TResult>> bind)
        {
            return maybe.HasValue
                ? bind(maybe.Value)
                : Maybe<TResult>.None;
        }

        public Maybe<TResult> SelectMany<TIntermediate, TResult>(
            Func<T, Maybe<TIntermediate>> bind,
            Func<T, TIntermediate, TResult> project)
        {
            if (!maybe.HasValue)
                return Maybe<TResult>.None;

            var intermediate = bind(maybe.Value);
            if (!intermediate.HasValue)
                return Maybe<TResult>.None;

            return project(maybe.Value, intermediate.Value);
        }

        public Maybe<T> Where(
            Func<T, bool> predicate)
        {
            return maybe.HasValue && predicate(maybe.Value)
                ? maybe
                : Maybe<T>.None;
        }
    }
}

//public static class MaybeAsyncExtensions
//{
//    public static async Task<Maybe<TResult>> Select<T, TResult>(
//        this Task<Maybe<T>> maybeTask,
//        Func<T, TResult> selector)
//    {
//        var maybe = await maybeTask;
//        return maybe.HasValue ? selector(maybe.Value) : Maybe<TResult>.None;
//    }

//    public static async Task<Maybe<TResult>> SelectMany<T, TResult>(
//        this Task<Maybe<T>> maybeTask,
//        Func<T, Task<Maybe<TResult>>> bind)
//    {
//        var maybe = await maybeTask;
//        return maybe.HasValue ? await bind(maybe.Value) : Maybe<TResult>.None;
//    }

//    public static async Task<Maybe<TResult>> SelectMany<T, TIntermediate, TResult>(
//        this Task<Maybe<T>> maybeTask,
//        Func<T, Task<Maybe<TIntermediate>>> bind,
//        Func<T, TIntermediate, TResult> project)
//    {
//        var maybe = await maybeTask;
//        if (!maybe.HasValue) return Maybe<TResult>.None;

//        var intermediate = await bind(maybe.Value);
//        if (!intermediate.HasValue) return Maybe<TResult>.None;

//        return project(maybe.Value, intermediate.Value);
//    }

//    public static async Task<Maybe<T>> Where<T>(
//        this Task<Maybe<T>> maybeTask,
//        Func<T, bool> predicate)
//    {
//        var maybe = await maybeTask;
//        return maybe.HasValue && predicate(maybe.Value) ? maybe : Maybe<T>.None;
//    }
//}
