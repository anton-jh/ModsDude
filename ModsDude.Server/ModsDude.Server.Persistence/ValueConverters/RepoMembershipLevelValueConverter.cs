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


    /// <summary>Holds <paramref name="column"/> to the names of defined levels, so no row can carry another.</summary>
    public static string CheckConstraintSql(string column)
    {
        var names = Enum.GetNames<RepoMembershipLevel>().Order(StringComparer.Ordinal).Select(x => $"'{x}'");

        return $"\"{column}\" IN ({string.Join(", ", names)})";
    }
}
