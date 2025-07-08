namespace ModsDude.WindowsClient.Model.DynamicForms;

[AttributeUsage(AttributeTargets.Property)]
public class CanBeModifiedAttribute(bool canBeModified = true) : Attribute
{
    public bool CanBeModified { get; } = canBeModified;
}
