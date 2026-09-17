using ITBees.RestfulApiControllers.Exceptions;
using ITBees.UserManager.Interfaces;
using Microsoft.AspNetCore.Http;

namespace ITBees.Printers.Services;

internal static class PrintingAccess
{
    /// <summary>
    /// Guid of the logged-in user, after checking <see cref="PrintersSettings.RequiredRole"/>.
    /// Everything in this module is scoped to that guid - a user never sees or uses the
    /// agents, printers, settings or jobs of anybody else.
    /// </summary>
    public static Guid GetPrintingUserGuid(this IAspCurrentUserService currentUserService, PrintersSettings settings)
    {
        var userGuid = currentUserService.GetCurrentUserGuid();
        if (userGuid == null || userGuid == Guid.Empty)
        {
            throw new FasApiErrorException("Zaloguj się, aby korzystać z drukowania.", StatusCodes.Status401Unauthorized);
        }

        if (!string.IsNullOrWhiteSpace(settings.RequiredRole) &&
            !currentUserService.CurrentUserIsInRole(settings.RequiredRole))
        {
            throw new FasApiErrorException("Nie masz uprawnień do korzystania z drukowania.",
                StatusCodes.Status403Forbidden);
        }

        return userGuid.Value;
    }
}
