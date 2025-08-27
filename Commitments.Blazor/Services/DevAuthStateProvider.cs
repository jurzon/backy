using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace CommitmentsBlazor.Services;

public class DevAuthStateProvider : AuthenticationStateProvider
{
    private ClaimsPrincipal _current;

    public DevAuthStateProvider()
    {
        _current = BuildPrincipal("dev", "11111111-1111-1111-1111-111111111111");
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
        => Task.FromResult(new AuthenticationState(_current));

    public void SetUser(string name, Guid id)
    {
        _current = BuildPrincipal(name, id.ToString());
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsPrincipal BuildPrincipal(string name, string id)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.NameIdentifier, id)
        };
        var identity = new ClaimsIdentity(claims, "BasicDev");
        return new ClaimsPrincipal(identity);
    }
}
