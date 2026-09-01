namespace ModsDude.Client.Core.Services;

/// <summary>
/// State that belongs to the signed-in user rather than to the machine, and so has to be dropped
/// when the user changes.
/// </summary>
/// <remarks>
/// The distinction is the whole point. Local instances, content stores and the image cache describe
/// the game installations on this PC and do not change with who is signed in, so they are
/// deliberately not this. Repos and profiles came out of one account's memberships and are
/// meaningless to the next account.
/// </remarks>
public interface IUserScopedState
{
    void ClearUserState();
}
