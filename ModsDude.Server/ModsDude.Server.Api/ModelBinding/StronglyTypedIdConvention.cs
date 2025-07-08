using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ModsDude.Server.Api.ModelBinding;

public class StronglyTypedIdConvention : IParameterModelConvention
{
    public void Apply(ParameterModel parameter)
    {
        // Check if the parameter is a strongly typed ID
        if (IsStronglyTypedId(parameter.ParameterInfo.ParameterType))
        {
            // Set the binding source to Route by default, adjust as needed
            parameter.BindingInfo = parameter.BindingInfo ?? new BindingInfo
            {
                BindingSource = BindingSource.Path // or BindingSource.Query, BindingSource.Header, etc.
            };
        }
    }

    private bool IsStronglyTypedId(Type type)
    {
        // Check if the type is a struct, has a single property "Value" and no other fields
        return type.IsValueType &&
               type.GetProperty("Value") != null &&
               type.GetProperties().Length == 1;
    }
}
