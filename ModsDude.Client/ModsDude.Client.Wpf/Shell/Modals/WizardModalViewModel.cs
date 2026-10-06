using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Runtime.ExceptionServices;

namespace ModsDude.Client.Wpf.Shell.Modals;

/// <summary>
/// Works out the step that follows an answered one, or null where that answer was the last.
/// </summary>
public delegate Task<WizardStepViewModel?> WizardNext(WizardStepViewModel answered, CancellationToken cancellationToken);


/// <summary>
/// A chain of questions in one modal, one step at a time, with a way back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The steps are worked out from the answers, never listed up front.</b> Each answer goes to
/// <see cref="WizardNext"/>, which looks at it and at the state of things now, so a question that no
/// longer follows simply is not asked.
/// </para>
/// <para>
/// <b>Back returns to the earlier step as it was answered.</b> Going forward from it works out the
/// later steps again.
/// </para>
/// <para>
/// <b>Nothing is done until the last answer.</b> The caller does the work after
/// <see cref="ShowAsync"/> returns true, so Back and Cancel are always free.
/// </para>
/// </remarks>
public sealed partial class WizardModalViewModel : ModalViewModel
{
    private readonly WizardNext _next;
    private readonly CancellationToken _lifetime;
    private readonly Stack<WizardStepViewModel> _earlier = new();

    private CancellationTokenSource? _advancing;
    private ExceptionDispatchInfo? _failure;


    /// <param name="lifetime">The caller's, so a page navigated away from stops the step being worked out.</param>
    public WizardModalViewModel(WizardStepViewModel first, WizardNext next, CancellationToken lifetime)
    {
        _next = next;
        _lifetime = lifetime;
        _current = first;

        first.PropertyChanged += OnStepChanged;
    }


    /// <summary>A wizard of one step, for a question that is sometimes asked on its own.</summary>
    public static WizardModalViewModel Single(WizardStepViewModel step, CancellationToken lifetime)
        => new(step, (_, _) => Task.FromResult<WizardStepViewModel?>(null), lifetime);


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEarlier))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand))]
    private WizardStepViewModel _current;

    /// <summary>While the next step is being worked out, which can mean reading the disk.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand))]
    private bool _isAdvancing;

    public bool HasEarlier => _earlier.Count > 0;

    /// <summary>Whether the last step was answered, which is what lets the caller go ahead.</summary>
    public bool Committed { get; private set; }


    /// <summary>
    /// Shows the wizard and waits for it to end.
    /// </summary>
    /// <returns>Whether every step was answered.</returns>
    /// <exception cref="Exception">Whatever stopped a step being worked out, rethrown for the caller to report.</exception>
    public async Task<bool> ShowAsync(IModalService modals)
    {
        await modals.Show(this);

        _failure?.Throw();

        return Committed;
    }


    [RelayCommand(CanExecute = nameof(CanChoose))]
    private async Task Choose(WizardChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        choice.Choose();

        using var advancing = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);

        _advancing = advancing;
        IsAdvancing = true;

        try
        {
            var next = await _next(Current, advancing.Token);

            // Cancelled while the step was being worked out.
            if (Done)
            {
                return;
            }

            if (next is null)
            {
                Committed = true;
                Done = true;

                return;
            }

            _earlier.Push(Current);
            Current = next;
        }
        catch (OperationCanceledException) when (advancing.IsCancellationRequested)
        {
            Done = true;
        }
        catch (Exception exception)
        {
            // Handed to the caller through ShowAsync, which reports it the way it reports its own work.
            _failure = ExceptionDispatchInfo.Capture(exception);
            Done = true;
        }
        finally
        {
            _advancing = null;
            IsAdvancing = false;
        }
    }

    private bool CanChoose(WizardChoice? choice) => IsAdvancing is false && choice is { CanChoose: true };

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Current = _earlier.Pop();

    private bool CanGoBack() => IsAdvancing is false && _earlier.Count > 0;

    [RelayCommand]
    private void Cancel()
    {
        _advancing?.Cancel();
        Done = true;
    }


    public override bool TryCancel() => Press(CancelCommand);

    public override bool TryAccept()
    {
        if (Current.Choices.FirstOrDefault(x => x.IsDefault) is not WizardChoice choice || CanChoose(choice) is false)
        {
            return false;
        }

        ChooseCommand.Execute(choice);

        return true;
    }


    partial void OnCurrentChanged(WizardStepViewModel? oldValue, WizardStepViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnStepChanged;
        }

        newValue.PropertyChanged += OnStepChanged;
    }

    private void OnStepChanged(object? sender, PropertyChangedEventArgs e) => ChooseCommand.NotifyCanExecuteChanged();
}
