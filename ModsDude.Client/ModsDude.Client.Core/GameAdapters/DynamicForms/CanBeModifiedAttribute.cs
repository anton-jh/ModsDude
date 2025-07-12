namespace ModsDude.Client.Core.GameAdapters.DynamicForms;

[AttributeUsage(AttributeTargets.Property)]
public class CanBeModifiedAttribute(bool canBeModified = true) : Attribute
{
    public bool CanBeModified { get; } = canBeModified;
}
