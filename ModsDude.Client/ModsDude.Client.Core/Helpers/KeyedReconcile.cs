using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// Brings a list of view models in line with the values it should show, keeping the view model of
/// every key that stays. A row that is updated rather than replaced keeps its container, so a popup
/// open on it stays open and focus stays where it was.
/// </summary>
public static class KeyedReconcile
{
    public static void Apply<TItem, TValue, TKey>(
        ObservableCollection<TItem> items,
        IReadOnlyList<TValue> values,
        Func<TValue, TKey> keyOfValue,
        Func<TItem, TKey> keyOfItem,
        Func<TValue, TItem> create,
        Action<TItem, TValue> update)
        where TKey : notnull
    {
        var wanted = values.Select(keyOfValue).ToHashSet();

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (wanted.Contains(keyOfItem(items[i])) is false)
            {
                items.RemoveAt(i);
            }
        }

        for (var i = 0; i < values.Count; i++)
        {
            var key = keyOfValue(values[i]);
            var existing = IndexOf(items, key, keyOfItem, from: i);

            if (existing < 0)
            {
                items.Insert(i, create(values[i]));

                continue;
            }

            if (existing != i)
            {
                items.Move(existing, i);
            }

            update(items[i], values[i]);
        }
    }

    private static int IndexOf<TItem, TKey>(ObservableCollection<TItem> items, TKey key, Func<TItem, TKey> keyOfItem, int from)
        where TKey : notnull
    {
        for (var i = from; i < items.Count; i++)
        {
            if (EqualityComparer<TKey>.Default.Equals(keyOfItem(items[i]), key))
            {
                return i;
            }
        }

        return -1;
    }
}
