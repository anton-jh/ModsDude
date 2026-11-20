using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace ModsDude.Server.Api.Accounts;

/// <summary>What every account page needs: safe return addresses and links for emails.</summary>
public abstract class AccountPageModel : PageModel
{
    /// <summary>
    /// The return address where it stays on this server, otherwise null. Anything else could send a
    /// signed-in browser wherever a crafted link pointed.
    /// </summary>
    protected string? LocalOnly(string? returnUrl)
        => returnUrl is not null && Url.IsLocalUrl(returnUrl) ? returnUrl : null;

    protected IActionResult RedirectToReturnUrl(string? returnUrl)
        => LocalRedirect(LocalOnly(returnUrl) ?? AccountPagePaths.Home);

    protected string AbsolutePageUrl(string pageName, object values)
        => Url.Page(pageName, pageHandler: null, values, Request.Scheme)
            ?? throw new InvalidOperationException($"No page is named '{pageName}'.");

    /// <summary>Identity's tokens hold characters that do not survive a query string as they are.</summary>
    protected static string EncodeToken(string token)
        => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    protected static string? DecodeToken(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
