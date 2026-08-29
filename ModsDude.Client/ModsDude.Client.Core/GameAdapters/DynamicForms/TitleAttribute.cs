namespace ModsDude.Client.Core.GameAdapters.DynamicForms;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class TitleAttribute(string text) : Attribute
{
    public string Text { get; init; } = text;
}
