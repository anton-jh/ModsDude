namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// Carries what sync needs of each version, because sync reads a profile's pins rather than the
/// repo's mod list; without them here every sync would have to pull the unpaged mod list.
/// </summary>
/// <param name="FileName">What the file has to be called in the mod folder, in its registered casing.</param>
/// <param name="SizeBytes">What an apply will download is the sum of the sizes of what it has not got.</param>
/// <param name="Added">
/// When the mod arrived at this version - see <see cref="Domain.Profiles.ProfileModPin.Added"/>. What
/// the editor's right list sorts by.
/// </param>
public record ProfileModPinDto(string ModId, string ModVersionId, string FileName, string ContentHash, long SizeBytes, bool Locked, DateTime Added);
