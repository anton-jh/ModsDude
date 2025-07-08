using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ModsDude.Server.Domain.RepoMemberships;

namespace ModsDude.Server.Persistence.ValueConverters;
internal class RepoMembershipLevelValueConverter : ValueConverter<RepoMembershipLevel, string>
{
    public RepoMembershipLevelValueConverter()
        : base(
            model => model.ToString(),
            provider => (RepoMembershipLevel)Enum.Parse(typeof(RepoMembershipLevel), provider))
    {
    }
}
