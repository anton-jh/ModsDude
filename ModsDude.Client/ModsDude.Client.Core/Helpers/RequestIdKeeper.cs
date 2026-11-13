namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// The request ID of one action the user is making. Sending the same request again, after its answer
/// was lost, sends the same ID, so the server answers it as it answered the original. A different
/// request, or one sent after <see cref="Forget"/>, gets a new random ID.
/// </summary>
/// <param name="comparer">What makes two requests the same one. Value equality where omitted.</param>
public sealed class RequestIdKeeper<TRequest>(IEqualityComparer<TRequest>? comparer = null)
{
    private readonly IEqualityComparer<TRequest> _comparer = comparer ?? EqualityComparer<TRequest>.Default;
    private readonly Lock _lock = new();
    private (TRequest Request, Guid Id)? _last;


    public Guid For(TRequest request)
    {
        lock (_lock)
        {
            if (_last is { } last && _comparer.Equals(last.Request, request))
            {
                return last.Id;
            }

            var id = Guid.NewGuid();
            _last = (request, id);

            return id;
        }
    }

    /// <summary>Called once the request is answered, so the next one is a new request even where it looks the same.</summary>
    public void Forget()
    {
        lock (_lock)
        {
            _last = null;
        }
    }
}
