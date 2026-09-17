using System.Security.Claims;
using System.Text.Encodings.Web;
using ITBees.Models.Users;
using ITBees.UserManager.Interfaces;
using ITBees.UserManager.Interfaces.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ITBees.Printers.DevHost;

/// <summary>The sandbox has exactly one user, and that user is always logged in.</summary>
public static class DevUser
{
    public const string Scheme = "DevUser";
    public static readonly Guid Guid = new("11111111-2222-3333-4444-555555555555");
}

public class DevUserAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DevUserAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, DevUser.Guid.ToString()) },
            DevUser.Scheme);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), DevUser.Scheme)));
    }
}

public class DevCurrentUserService : IAspCurrentUserService
{
    public Guid? GetCurrentUserGuid() => DevUser.Guid;
    public bool CurrentUserIsPlatformOperator() => true;
    public bool CurrentUserIsInRole(string role) => true;
    public bool CurrentUserIsInRole(string role, Guid companyGuid) => true;

    public CurrentUser GetCurrentUser() => throw new NotSupportedException();
    public CurrentSessionUser GetCurrentSessionUser() => throw new NotSupportedException();
    public TypeOfOperation GetMyAcceessToCompany(Guid companyGuid) => throw new NotSupportedException();
    public bool TryCanIDoForCompany(TypeOfOperation typeOfOperation, Guid companyGuid) =>
        throw new NotSupportedException();
}
