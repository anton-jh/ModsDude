using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.ComponentModel;

namespace ModsDude.Client.Core.Models;

/// <summary>
/// One profile as the server last described it. Updated in place, so everything bound to it - its
/// sidebar entry, its open pages - follows a rename or a new revision without being rebuilt.
/// </summary>
public sealed class Profile : INotifyPropertyChanged
{
    public Profile(ProfileDto dto)
    {
        Id = dto.Id;
        RepoId = dto.RepoId;
        Name = dto.Name;
        HeadRevision = dto.HeadRevision;
        ArchivedAt = dto.ArchivedAt;
    }


    public event PropertyChangedEventHandler? PropertyChanged;


    public Guid Id { get; }
    public Guid RepoId { get; }
    public string Name { get; private set; }

    /// <summary>The newest revision, which is the one a save is based on and a game following the profile installs.</summary>
    public int HeadRevision { get; private set; }

    /// <summary>When it was archived, or null for a live profile.</summary>
    public DateTime? ArchivedAt { get; private set; }


    /// <returns>Whether anything changed.</returns>
    internal bool Apply(ProfileDto dto)
    {
        var changed = false;

        if (Name != dto.Name)
        {
            Name = dto.Name;
            PropertyChanged?.Invoke(this, new(nameof(Name)));
            changed = true;
        }

        if (ArchivedAt != dto.ArchivedAt)
        {
            ArchivedAt = dto.ArchivedAt;
            PropertyChanged?.Invoke(this, new(nameof(ArchivedAt)));
            changed = true;
        }

        return ApplyHeadRevision(dto.HeadRevision) || changed;
    }

    /// <returns>Whether the revision moved.</returns>
    internal bool ApplyHeadRevision(int number)
    {
        if (HeadRevision == number)
        {
            return false;
        }

        HeadRevision = number;
        PropertyChanged?.Invoke(this, new(nameof(HeadRevision)));

        return true;
    }
}
