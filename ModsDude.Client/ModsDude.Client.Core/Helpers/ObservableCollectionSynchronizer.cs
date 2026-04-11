using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq.Expressions;

namespace ModsDude.Client.Core.Helpers;

public sealed class ObservableCollectionSynchronizer<TSource, TTarget, TKey> : IDisposable
    where TSource : notnull
    where TTarget : notnull
{
    private readonly ObservableCollection<TTarget> _target;
    private readonly ObservableCollection<TSource> _source;
    private readonly Dictionary<TSource, TTarget> _map = [];
    private readonly Dictionary<TTarget, PropertyChangedEventHandler> _propertyHandlers = [];
    private readonly Func<TSource, TTarget> _factory;
    private readonly Func<TTarget, TKey> _keySelector;
    private readonly IComparer<TKey> _comparer;
    private readonly Func<TSource, bool> _filter;
    private readonly string? _propertyName;
    private readonly NotifyCollectionChangedEventHandler _collectionChangedHandler;

    private bool _disposed;


    public ObservableCollectionSynchronizer(
        ObservableCollection<TSource> source,
        ObservableCollection<TTarget> target,
        Func<TSource, TTarget> factory,
        Expression<Func<TTarget, TKey>> keySelectorExpression,
        IComparer<TKey>? comparer = null,
        Func<TSource, bool>? filter = null,
        bool targetAlreadyInitialized = false)
    {
        _source = source;
        _target = target;
        _factory = factory;

        _keySelector = keySelectorExpression.Compile();
        _propertyName = GetPropertyName(keySelectorExpression);

        _comparer = comparer ?? Comparer<TKey>.Default;
        _filter = filter ?? (_ => true);

        if (targetAlreadyInitialized)
        {
            foreach (var item in source)
            {
                Map(item);
            }
        }
        else
        {
            foreach (var item in source)
            {
                Add(item);
            }
        }

        _collectionChangedHandler = (s, e) =>
        {
            if (e.NewItems != null)
                foreach (TSource item in e.NewItems)
                    Add(item);

            if (e.OldItems != null)
                foreach (TSource item in e.OldItems)
                    Remove(item);

            if (e.Action == NotifyCollectionChangedAction.Reset)
                ClearAll();
        };

        _source.CollectionChanged += _collectionChangedHandler;
    }


    private void Map(TSource model)
    {
        var vm = _factory(model);
        _map[model] = vm;
    }


    private void Add(TSource model)
    {
        if (!_filter(model))
        {
            return;
        }

        var vm = _factory(model);
        _map[model] = vm;

        if (vm is INotifyPropertyChanged npc)
        {
            void handler(object? _, PropertyChangedEventArgs e)
            {
                if (_propertyName == null || e.PropertyName == _propertyName)
                    Resort(vm);
            }

            npc.PropertyChanged += handler;
            _propertyHandlers[vm] = handler;
        }

        int index = FindInsertIndex(vm);
        _target.Insert(index, vm);
    }


    private void Remove(TSource model)
    {
        if (_map.TryGetValue(model, out var vm))
        {
            if (vm is INotifyPropertyChanged npc &&
                _propertyHandlers.TryGetValue(vm, out var handler))
            {
                npc.PropertyChanged -= handler;
                _propertyHandlers.Remove(vm);
            }

            _target.Remove(vm);
            _map.Remove(model);
        }
    }


    private void ClearAll()
    {
        foreach (var (vm, handler) in _propertyHandlers)
        {
            if (vm is INotifyPropertyChanged npc)
                npc.PropertyChanged -= handler;
        }

        _propertyHandlers.Clear();
        _map.Clear();
        _target.Clear();
    }


    private void Resort(TTarget vm)
    {
        if (!_target.Contains(vm))
            return;

        _target.Remove(vm);

        int index = FindInsertIndex(vm);
        _target.Insert(index, vm);
    }


    private int FindInsertIndex(TTarget vm)
    {
        var key = _keySelector(vm);

        for (int i = 0; i < _target.Count; i++)
        {
            var existingKey = _keySelector(_target[i]);

            if (_comparer.Compare(key, existingKey) < 0)
                return i;
        }

        return _target.Count;
    }


    private static string? GetPropertyName(Expression<Func<TTarget, TKey>> expr)
    {
        if (expr.Body is MemberExpression member)
            return member.Member.Name;

        if (expr.Body is UnaryExpression unary && unary.Operand is MemberExpression inner)
            return inner.Member.Name;

        return null;
    }


    public void Dispose()
    {
        if (_disposed)
            return;

        _source.CollectionChanged -= _collectionChangedHandler;
        ClearAll();

        _disposed = true;
    }
}
