namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// Where a version sits among its siblings: between these two, both of which are asserted against
/// the ordering as it stands. The client computes the position with its own adapter's comparer — the
/// server has no adapters and cannot parse a version string.
/// </summary>
/// <remarks>
/// Shared by registration and by the move, because they are the same statement about an ordering
/// made at two different times, and a client that can compute one can compute the other. A null
/// <paramref name="After"/> means the version goes first, a null <paramref name="Before"/> that it
/// goes last, and both null that the mod has no other versions.
/// </remarks>
public record ModVersionPlacement(string? After, string? Before);
