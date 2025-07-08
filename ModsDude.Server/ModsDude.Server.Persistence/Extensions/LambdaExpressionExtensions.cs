using System.Linq.Expressions;

namespace ModsDude.Server.Persistence.Extensions;

/// <summary>
/// https://stackoverflow.com/a/27463773
/// Adapted for nullability
/// </summary>
internal static class LambdaExpressionExtensions
{
    public static Expression<Func<TInput, object?>> ToUntypedPropertyExpression<TInput, TOutput>(this Expression<Func<TInput, TOutput>> expression)
    {
        var memberName = ((MemberExpression)expression.Body).Member.Name;

        var param = Expression.Parameter(typeof(TInput));
        var field = Expression.Property(param, memberName);
        return Expression.Lambda<Func<TInput, object?>>(field, param);
    }
}
