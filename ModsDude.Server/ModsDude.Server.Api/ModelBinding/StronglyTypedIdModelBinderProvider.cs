using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ModsDude.Server.Api.ModelBinding;

public class StronglyTypedIdModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var modelType = context.Metadata.ModelType;

        // Check if the model type has a single property named "Value"
        if (modelType.IsValueType && modelType.GetProperty("Value") != null)
        {
            return new BinderTypeModelBinder(typeof(StronglyTypedIdModelBinder));
        }

        return null;
    }
}
