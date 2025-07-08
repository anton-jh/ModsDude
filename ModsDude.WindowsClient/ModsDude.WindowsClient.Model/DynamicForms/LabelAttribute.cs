namespace ModsDude.WindowsClient.Model.DynamicForms;

[AttributeUsage(AttributeTargets.Property)]
public class LabelAttribute(string text) : Attribute
{
    public string Text { get; init; } = text;
}
