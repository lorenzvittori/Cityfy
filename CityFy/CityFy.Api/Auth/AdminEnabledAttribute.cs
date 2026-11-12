using CityFy.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace CityFy.Api.Auth;

/// <summary>Admin endpoints answer 404 when the panel is disabled (Admin:Enabled=false), as if they did not exist.</summary>
public sealed class AdminEnabledAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value.Enabled)
            context.Result = new NotFoundResult();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
