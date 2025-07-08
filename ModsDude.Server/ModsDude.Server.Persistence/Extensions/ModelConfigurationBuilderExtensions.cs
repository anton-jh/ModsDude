using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Reflection;

namespace ModsDude.Server.Persistence.Extensions;
internal static class ModelConfigurationBuilderExtensions
{
    public static ModelConfigurationBuilder ConfigureValueObjectConversionsFromAssembly(this ModelConfigurationBuilder builder, Assembly assembly)
    {
        var types = assembly.GetTypes()
            .Where(x => x.IsValueType && !x.IsEnum && HasValueProperty(x));

        foreach (var type in types)
        {
            var innerType = type.GetProperty("Value")!.PropertyType;
            var converterType = typeof(ValueObjectConverter<,>).MakeGenericType(type, innerType);

            builder.Properties(type)
                .HaveConversion(converterType);
        }

        return builder;
    }


    private static bool HasValueProperty(Type type)
    {
        return type.GetProperty("Value") is not null;
    }


    private class ValueObjectConverter<TModel, TProvider>()
        : ValueConverter<TModel, TProvider>(
            x => (TProvider)typeof(TModel).GetProperty("Value")!.GetValue(x)!,
            x => (TModel)typeof(TModel).GetConstructor(new Type[] { typeof(TProvider) })!.Invoke(new object?[] { x }));
}
