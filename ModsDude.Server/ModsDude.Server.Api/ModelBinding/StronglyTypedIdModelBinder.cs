using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ModsDude.Server.Api.ModelBinding;

public class StronglyTypedIdModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext == null)
            throw new ArgumentNullException(nameof(bindingContext));

        var modelType = bindingContext.ModelType;
        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

        if (valueProviderResult == ValueProviderResult.None)
        {
            bindingContext.Result = ModelBindingResult.Failed();
            return Task.CompletedTask;
        }

        var value = valueProviderResult.FirstValue;

        if (string.IsNullOrEmpty(value))
        {
            bindingContext.Result = ModelBindingResult.Failed();
            return Task.CompletedTask;
        }

        try
        {
            var underlyingType = modelType.GetProperty("Value")!.PropertyType;
            var constructor = modelType.GetConstructor([underlyingType]);

            if (constructor == null)
            {
                bindingContext.Result = ModelBindingResult.Failed();
                return Task.CompletedTask;
            }

            // Convert the input value to the underlying type of the strongly typed ID
            var typedValue = Convert.ChangeType(value, underlyingType);
            var model = constructor.Invoke([typedValue]);

            bindingContext.Result = ModelBindingResult.Success(model);
        }
        catch (Exception)
        {
            bindingContext.Result = ModelBindingResult.Failed();
        }

        return Task.CompletedTask;
    }
}
