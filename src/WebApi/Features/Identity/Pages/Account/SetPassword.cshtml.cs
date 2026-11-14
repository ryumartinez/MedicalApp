using MedicalApp.Common.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace MedicalApp.Features.Identity.Pages.Account;

/// <summary>
/// Sets an account's password through a single-use setup link. A successful change updates the security stamp,
/// which invalidates the link and ends the account's other sign-in sessions.
/// </summary>
[AllowAnonymous]
public class SetPasswordModel(UserManager<ApplicationUser> users, IOptions<IdentityOptions> identityOptions) : PageModel
{
    public const string InvalidLinkMessage = "This link was already used, was replaced by a newer link, or has expired. Ask an administrator for a new link.";

    [BindProperty(SupportsGet = true, Name = "user")]
    public string? UserId { get; set; }

    [BindProperty(SupportsGet = true, Name = "token")]
    public string? Token { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Completed { get; private set; }
    public bool LinkInvalid { get; private set; }
    public List<string> Errors { get; } = [];
    public int MinimumLength => identityOptions.Value.Password.RequiredLength;

    public async Task OnGetAsync()
    {
        ProtectLink();
        var user = await FindUserAsync();
        LinkInvalid = user is null || !await users.VerifyUserTokenAsync(user, users.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose, Token!);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ProtectLink();
        var user = await FindUserAsync();
        if (user is null)
        {
            LinkInvalid = true;
            return Page();
        }

        if (Input.Password != Input.ConfirmPassword)
        {
            Errors.Add("The passwords do not match.");
            return Page();
        }

        var result = await users.ResetPasswordAsync(user, Token!, Input.Password);
        if (result.Succeeded)
        {
            Completed = true;
            return Page();
        }

        if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            LinkInvalid = true;
        else
            Errors.AddRange(result.Errors.Select(error => error.Description));

        return Page();
    }

    private async Task<ApplicationUser?> FindUserAsync() =>
        Guid.TryParse(UserId, out var id) && !string.IsNullOrEmpty(Token) ? await users.FindByIdAsync(id.ToString()) : null;

    // The query string holds the setup token. Keep it out of referrers and caches.
    private void ProtectLink()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.CacheControl = "no-store";
    }

    public sealed class InputModel
    {
        public string Password { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
    }
}
