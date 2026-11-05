using GeoConquest.Models.Domain;
using GeoConquest.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GeoConquest.Security;

/// <summary>Marks pages a logged-in user may open before giving POPIA consent (the notice itself, logout, errors).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowWithoutConsentAttribute : Attribute
{
}

/// <summary>
/// POPIA gate: a logged-in user who has not accepted the current privacy notice is sent to the consent page
/// before anything else. Visitors who are not logged in are not affected (we hold no personal data about them).
/// </summary>
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
