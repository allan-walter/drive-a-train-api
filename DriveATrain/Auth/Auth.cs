using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace DriveATrain.Auth;

public static class AuthEndpoint
{
    public record LoginBody(string Email, string Password);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("auth/logout", (HttpContext context, SignInManager<IdentityUser> signInManager) =>
        {
            signInManager.SignOutAsync();

            return Results.Ok();
        });

        app.MapPost("auth/login",
            async (HttpContext context, SignInManager<IdentityUser> signInManager,
                UserManager<IdentityUser> userManager, LoginBody body) =>
            {
                var user = await userManager.FindByEmailAsync(body.Email);

                if (user == null)
                {
                    return Results.Unauthorized();
                }

                var result = await signInManager.PasswordSignInAsync(
                    user,
                    body.Password,
                    isPersistent: true, // remember me cookie
                    lockoutOnFailure: false);

                if (result.Succeeded)
                    return Results.Ok();

                return Results.Unauthorized();
            });
    }
}