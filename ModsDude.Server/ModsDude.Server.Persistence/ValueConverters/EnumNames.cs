namespace ModsDude.Server.Persistence.ValueConverters;

/// <summary>An enum stored by name, held to the names it defines.</summary>
internal static class EnumNames
{
    public static string CheckConstraintSql<TEnum>(string column)
        where TEnum : struct, Enum
    {
        var names = Enum.GetNames<TEnum>().Order(StringComparer.Ordinal).Select(x => $"'{x}'");

        return $"\"{column}\" IN ({string.Join(", ", names)})";
    }
}
