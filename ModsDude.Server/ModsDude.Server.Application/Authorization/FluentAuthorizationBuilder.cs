using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Application.Authorization;

public class FluentAuthorizationBuilder(
    User user)
{
    private AuthorizationResult? _result;


    public User User { get; } = user;

    public AuthorizationResult? Result
    {
        get => _result;
        set
        {
            _result ??= value;
        }
    }
}
