using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Server.AspNetCore;

namespace MedicalApp.Features.Identity.Pages.Account;

[AllowAnonymous]
public class ErrorModel : PageModel
{
    public string? Description { get; private set; }

    public void OnGet() => Description = HttpContext.GetOpenIddictServerResponse()?.ErrorDescription;
}
