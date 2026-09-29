using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// A collection that can be made to hold a given list. A small difference is applied item by item, so a
/// list control keeps its scroll position; a large one is one reset rather than thousands of events.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
    where T : class
{
    private const int _incrementalLimit = 64;


    public void Reconcile(IReadOnlyList<T> target)
    {
        var wanted = new HashSet<T>(target, ReferenceEqualityComparer.Instance);
        var present = new HashSet<T>(Items, ReferenceEqualityComparer.Instance);

        var changes = Items.Count(x => wanted.Contains(x) is false) + target.Count(x => present.Contains(x) is false);

        if (changes > _incrementalLimit)
        {
            ReplaceAll(target);

            return;
        }

        for (var index = Count - 1; index >= 0; index--)
        {
            if (wanted.Contains(this[index]) is false)
            {
                RemoveAt(index);
            }
        }

        for (var index = 0; index < target.Count; index++)
        {
            if (index < Count && ReferenceEquals(this[index], target[index]))
            {
                continue;
            }

            if (++changes > _incrementalLimit)
            {
                ReplaceAll(target);

                return;
            }

            var from = FindFrom(target[index], index + 1);

            if (from >= 0)
            {
                Move(from, index);
            }
            else
            {
                Insert(index, target[index]);
            }
        }
    }


    private int FindFrom(T item, int start)
    {
        for (var index = start; index < Count; index++)
        {
            if (ReferenceEquals(this[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    private void ReplaceAll(IReadOnlyList<T> items)
    {
        Items.Clear();

        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
