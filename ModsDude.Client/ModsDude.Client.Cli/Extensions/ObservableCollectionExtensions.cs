using System.Collections.ObjectModel;

namespace ModsDude.Client.Cli.Extensions;
public static class ObservableCollectionExtensions
{
    public static void InsertSorted<T, TKey>(
        this ObservableCollection<T> collection,
        T item,
        Func<T, TKey> keySelector,
        IComparer<TKey>? comparer = null)
    {
        comparer ??= Comparer<TKey>.Default;
        var itemKey = keySelector(item);

        for (int i = 0; i < collection.Count; i++)
        {
            var currentKey = keySelector(collection[i]);
            if (comparer.Compare(itemKey, currentKey) < 0)
            {
                collection.Insert(i, item);
                return;
            }
        }

        collection.Add(item);
    }
}
