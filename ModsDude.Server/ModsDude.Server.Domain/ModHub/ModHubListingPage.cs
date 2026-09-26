namespace ModsDude.Server.Domain.ModHub;

/// <summary>One page of ModHub's "latest" listing.</summary>
/// <param name="ModIds">The mods in the grid, in order, featured mods and paid DLCs left out.</param>
/// <param name="IsPastEnd">
/// Whether the grid is empty. Not the same as <paramref name="ModIds"/> being empty: ModHub mixes its
/// paid DLCs into the grid, so a page can hold nothing but DLCs and still have more mods after it.
/// </param>
public record ModHubListingPage(IReadOnlyList<int> ModIds, bool IsPastEnd);
