namespace ModsDude.Server.Api.Dtos;

/// <param name="Name">
/// What to call it on the way back, or null to keep the name it had. An archived entity gave up its
/// name when it was archived, so restoring is where a clash with something live has to be resolved -
/// and the only moment somebody is present to decide.
/// </param>
public record RestoreRequest(string? Name);
