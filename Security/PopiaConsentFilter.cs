using GeoConquest.Models.Domain;
using GeoConquest.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GeoConquest.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowWithoutConsentAttribute : Attribute
{
}

public sealed class PopiaConsentFilter : IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        bool exempt = context.ActionDescriptor.EndpointMetadata.OfType<AllowWithoutConsentAttribute>().Any();

        if (!exempt && user.Identity?.IsAuthenticated == true &&
            user.FindFirst(AccountService.ConsentClaim)?.Value != UserAccount.CurrentConsentVersion)
        {
            var request = context.HttpContext.Request;
            string returnUrl = HttpMethods.IsGet(request.Method) ? request.Path + request.QueryString : "/";
            context.Result = new RedirectToActionResult("Consent", "Account", new { returnUrl });
            return Task.CompletedTask;
        }

        return next();
    }
}
