using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using static ModsDude.Server.Api.ErrorHandling.Problems;

namespace ModsDude.Server.Api.ErrorHandling;

public class CustomProblemDetails : ProblemDetails
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required new ProblemType Type { get; init; }


    public CustomProblemDetails With(Action<CustomProblemDetails> modifyAction)
    {
        modifyAction(this);
        return this;
    }
}


public class CustomProblemDetails<T> : ProblemDetails
    where T : Enum
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required new T Type { get; init; }
}
