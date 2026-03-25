using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Reflection;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public partial class DynamicFormViewModel
    : ObservableObject, IDisposable
{
    private readonly DynamicForm _form;
    private readonly IDialogService _dialogService;
    private bool? _oldIsValid = null;


    public DynamicFormViewModel(
        bool editing,
        DynamicForm settings,
        IDialogService dialogService)
    {
        Editing = editing;
        _form = settings;
        _dialogService = dialogService;
        Fields = ExtractFields();

        foreach (var field in Fields)
        {
            field.PropertyChanged += OnFieldModified;
        }
    }


    public bool Editing { get; }

    public List<DynamicFormFieldViewModel> Fields { get; }

    public bool IsValid => _form.Validate().Length == 0;

    public event EventHandler? Modified;
    public event EventHandler? IsValidChanged;


    public DynamicForm ExtractResults()
    {
        var obj = (DynamicForm)Activator.CreateInstance(_form.GetType())!;

        foreach (var field in Fields)
        {
            field.Property.SetValue(obj, field.GetValue());
        }

        return obj;
    }

    public void Dispose()
    {
        foreach (var field in Fields)
        {
            field.PropertyChanged -= OnFieldModified;
        }
    }


    private List<DynamicFormFieldViewModel> ExtractFields()
    {
        return _form
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
                (string?)property.GetValue(_form) ?? "",
                _dialogService);
        }
        else
        {
            throw new InvalidOperationException($"Cannot handle dynamic form field of type '{property.PropertyType.FullName}'");
        }

        return field;
    }

    private void OnFieldModified(object? sender, EventArgs? e)
    {
        var valid = IsValid;
        if (valid != _oldIsValid)
        {
            _oldIsValid = valid;
            OnPropertyChanged(nameof(IsValid));
            IsValidChanged?.Invoke(this, EventArgs.Empty);
        }

        Modified?.Invoke(this, EventArgs.Empty);
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
