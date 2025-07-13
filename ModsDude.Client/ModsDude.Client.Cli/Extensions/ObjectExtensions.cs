namespace ModsDude.Client.Cli.Extensions;
internal static class ObjectExtensions
{
    public static T? If<T>(this T obj, bool condition)
    {
        return condition ? obj : default;
    }
}
