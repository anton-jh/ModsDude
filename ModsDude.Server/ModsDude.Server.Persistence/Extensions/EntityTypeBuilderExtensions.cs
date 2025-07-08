using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Common;
using System.Linq.Expressions;

namespace ModsDude.Server.Persistence.Extensions;
internal static class EntityTypeBuilderExtensions
{
    //public static EntityTypeBuilder<TEntity> HasGuidIdKeyWithConversion<TEntity, TProperty>(
    //    this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TProperty>> propertyExpression)
    //    where TEntity : class
    //    where TProperty : GuidId<TProperty>, new()
    //{
    //    return HasValueOfKeyWithConversion<TEntity, Guid, TProperty>(builder, propertyExpression);
    //}

    //public static EntityTypeBuilder<TEntity> HasValueOfKeyWithConversion<TEntity, TValue, TProperty>(
    //    this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TProperty>> propertyExpression)
    //    where TEntity : class
    //    where TProperty : ValueOf<TValue, TProperty>, new()
    //{
    //    var keyExpression = propertyExpression.ToUntypedPropertyExpression();

    //    builder.HasKey(keyExpression);

    //    builder.Property(propertyExpression)
    //        .HasValueOfConversion<TValue, TProperty>();

    //    return builder;
    //}
}
