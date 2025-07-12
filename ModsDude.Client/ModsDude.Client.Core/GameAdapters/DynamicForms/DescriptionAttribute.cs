namespace ModsDude.Client.Core.GameAdapters.DynamicForms;

[AttributeUsage(AttributeTargets.Property)]
public class DescriptionAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}
