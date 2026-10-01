using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Api.Dtos;
using System.Text.Json.Serialization;
using static ModsDude.Server.Api.ErrorHandling.Problems;

namespace ModsDude.Server.Api.ErrorHandling;

public class CustomProblemDetails : ProblemDetails
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required new ProblemType Type { get; init; }

    /// <summary>
    /// The SHA-256 recorded against a blob the caller was refused a link for, set only by
    /// <see cref="Problems.ModFileAlreadyPresent"/>. It sits on the shared type rather than on a
    /// derived one because a derived problem would need its own entry in the endpoint's
    /// <c>Results&lt;...&gt;</c> union at the same 400 status, and the OpenAPI document keeps one
    /// schema per status — the generated client would end up unable to read the one field the
    /// response exists to carry.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentHash { get; init; }

    /// <summary>
    /// Who holds a savegame the caller would take the claim of, set only by
    /// <see cref="Problems.SavegameClaimHeldByOther"/>. On the shared type for the reason
    /// <see cref="ContentHash"/> is.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SavegameCheckoutDto? Holder { get; init; }


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
