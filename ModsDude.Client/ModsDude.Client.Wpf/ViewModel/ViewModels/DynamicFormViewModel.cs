using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Reflection;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public class DynamicFormViewModel
{
    private readonly IDynamicForm _settings;
    private readonly IDialogService _dialogService;


    public DynamicFormViewModel(
        bool editing,
        IDynamicForm settings,
        IDialogService dialogService)
    {
        Editing = editing;
        _settings = settings;
        _dialogService = dialogService;
        Fields = ExtractFields();
    }


    public bool Editing { get; }

    public List<DynamicFormFieldViewModel> Fields { get; }


    public IDynamicForm ExtractResults()
    {
        var obj = (IDynamicForm)Activator.CreateInstance(_settings.GetType())!;

        foreach (var field in Fields)
        {
            field.Property.SetValue(obj, field.GetValue());
        }

        return obj;
    }


    private List<DynamicFormFieldViewModel> ExtractFields()
    {
        return _settings
            .GetType()
            .GetProperties()
            .Select(ExtractField)
            .ToList();
    }

    private DynamicFormFieldViewModel ExtractField(PropertyInfo property)
    {
        var title = property.GetCustomAttribute<TitleAttribute>()?.Text ?? property.Name;
        var required = property.GetCustomAttribute<RequiredAttribute>() is not null;
        var canBeModified = !Editing || property.GetCustomAttribute<CanBeModifiedAttribute>() is not null;

        DynamicFormFieldViewModel field;
        if (property.PropertyType == typeof(FolderPath))
        {
            field = new FolderPathDynamicFormFieldViewModel(
                property,
                title, required, canBeModified,
                (string?)property.GetValue(_settings) ?? "",
                _dialogService);
        }
        else
        {
            throw new InvalidOperationException($"Cannot handle dynamic form field of type '{property.PropertyType.FullName}'");
        }

        return field;
    }
}

public abstract class DynamicFormFieldViewModel(PropertyInfo property)
    : ObservableObject
{
    public PropertyInfo Property { get; } = property;
    public abstract object GetValue();
}

public partial class FolderPathDynamicFormFieldViewModel(
    PropertyInfo property,
    string title,
    bool required,
    bool canBeModified,
    string value,
    IDialogService dialogService)
    : DynamicFormFieldViewModel(property)
{
    public string Title { get; } = title;
    public bool Required { get; } = required;
    public bool CanBeModified { get; } = canBeModified;

    [ObservableProperty]
    private string _value = value;

    public override object GetValue()
    {
        return Value;
    }


    [RelayCommand]
    public void PickFolder()
    {
        var hint = string.IsNullOrWhiteSpace(Value) ? null : Value;

        if (dialogService.PickFolder(hint) is string folder)
        {
            Value = folder;
        }
    }
}
