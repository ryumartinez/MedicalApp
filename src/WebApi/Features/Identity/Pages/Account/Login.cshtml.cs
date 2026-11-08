using System.ComponentModel.DataAnnotations;
using MedicalApp.Common.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MedicalApp.Features.Identity.Pages.Account;

/// <summary>
/// Signs in with a username and password, then continues the pending authorization request.
/// Every failure shows the same message, so the page does not reveal which usernames exist.
/// </summary>
[AllowAnonymous]
public class LoginModel(AppSignInManager signIn) : PageModel
{
    public const string FailureMessage = "The username or password is incorrect, or this account cannot sign in right now.";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public void OnGet() => Response.Headers.CacheControl = "no-store";

    public async Task<IActionResult> OnPostAsync()
    {
        Response.Headers.CacheControl = "no-store";
        if (!ModelState.IsValid)
        {
            ErrorMessage = FailureMessage;
            return Page();
        }

        var result = await signIn.PasswordSignInAsync(Input.Username, Input.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ErrorMessage = FailureMessage;
            return Page();
        }

        return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : LocalRedirect(Host.Extensions.IdentityServerExt.SignedOutPath);
    }

    public sealed class InputModel
    {
        [Required]
        public string Username { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";
    }
}
