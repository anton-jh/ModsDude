namespace ModsDude.Client.Core.GameAdapters.DynamicForms;

[AttributeUsage(AttributeTargets.Property)]
public class TitleAttribute(string text) : Attribute
{
    public string Text { get; init; } = text;
}
