namespace ModsDude.Server.Api.Admin;

/// <summary>What <c>_Pager.cshtml</c> renders: where a page is, and how to link to another one.</summary>
public sealed record AdminPager(int Number, int Count, Func<int, string?> Link)
{
    public static AdminPager For<T>(AdminPage<T> page, Func<int, string?> link) => new(page.Number, page.Count, link);
}
