namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// What is holding a mod, or one version of it, in the repo.
/// </summary>
/// <remarks>
/// <para>
/// Read after a delete is refused, not before it is offered. The refusal is the server's answer and
/// the database's; this only exists to turn "something depends on it" into a list somebody can act
/// on, which is the difference between a dead end and a next step.
/// </para>
/// <para>
/// <b>Named per revision.</b> A version pinned once, hundreds of revisions ago, is as undeletable as
/// one pinned now - that is what the foreign key enforces - so naming only the profile would send
/// somebody to a history page with nothing to look for.
/// </para>
/// </remarks>
/// <param name="Truncated">
/// Whether the repo holds more than this lists. The cap is generous enough to be the whole answer in
/// every ordinary case, and saying so is better than a list that quietly stops.
/// </param>
public record ModDependentsDto(
    IEnumerable<ProfileDependentDto> Profiles,
    bool Truncated);

/// <param name="Revisions">
/// Which of this profile's revisions pin it, oldest first. Every one of them has to go before the
/// mod can, which is why the count is worth seeing before starting.
/// </param>
/// <param name="IncludesHead">
/// Whether one of them is the profile's current revision. That one cannot be deleted at all - the
/// mod has to be taken out of the profile and saved first - so it is the fact that decides whether
/// this is a tidy-up or a change to what people are running.
/// </param>
public record ProfileDependentDto(
    Guid ProfileId,
    string ProfileName,
    IEnumerable<int> Revisions,
    bool IncludesHead);
