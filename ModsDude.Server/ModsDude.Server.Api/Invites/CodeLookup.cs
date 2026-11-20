using ModsDude.Server.Api.ErrorHandling;
using System.Diagnostics.CodeAnalysis;

namespace ModsDude.Server.Api.Invites;

/// <summary>What a typed code was found to be, or the problem that says why it was not found.</summary>
public sealed class CodeLookup<T>
    where T : class
{
    private CodeLookup(T? value, CustomProblemDetails? problem)
    {
        Value = value;
        Problem = problem;
    }


    public T? Value { get; }

    public CustomProblemDetails? Problem { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Problem))]
    public bool IsFound => Value is not null;


    public static CodeLookup<T> Found(T value) => new(value, null);

    public static CodeLookup<T> NotFound(CustomProblemDetails problem) => new(null, problem);
}
