using EggIdentity.Contract;
using EggIncognito.Data.Services;
using EggIncognito.Models.ApiKeys;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/v1/keys")]
[EnableRateLimiting("write")]
[ApiAccess(ApiAccessLevel.Authenticated)]
public sealed class ApiKeysController(ICurrentUser currentUser, IConfiguration config, IServiceProvider services)
    : ControllerBase {
    private ApiKeyStore? Store => services.GetService(typeof(ApiKeyStore)) as ApiKeyStore;

    private int? Cap() => currentUser.IsAtLeast(UserRole.Contributor) || currentUser.IsSupporter
        ? null
        : config.GetValue("ApiKeys:MaxPerUser", 2);

    [HttpPost]
    public async Task<IActionResult> Mint([FromBody] MintReq req, CancellationToken ct) {
        var owner = currentUser.UserId;
        if (owner is null) return Unauthorized(new { error = "log in to mint an API key" });
        if (User.HasClaim(c => c.Type == ApiKeyGen.Claim))
            return StatusCode(403, new { error = "cannot mint keys using a key; use a logged-in session" });

        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });

        int? cap = Cap();
        if (cap is { } c && await store.ActiveCountAsync(owner.Value, ct) >= c)
            return Conflict(new { error = $"key limit reached ({c}); revoke one first" });

        (string full, string hash, string prefix) = ApiKeyGen.Mint();
        var row = await store.AddAsync(owner.Value, req.Name ?? "key", hash, prefix, ct);
        return Ok(new Minted(row.Id, row.Name, row.Prefix, full));
    }

    [HttpGet]
    public async Task<IActionResult> Mine(CancellationToken ct) {
        var owner = currentUser.UserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage keys" });
        var store = Store;
        if (store is null) return Ok(new KeysResponse([], Cap()));
        var rows = await store.ByOwnerAsync(owner.Value, ct);
        List<ApiKeysPanelRow> keys = [
            .. rows.Select(k =>
                new ApiKeysPanelRow(k.Id, k.Name, k.Prefix, k.CreatedAt, k.LastUsedAt, k.RequestCount, k.Revoked))
        ];
        return Ok(new KeysResponse(keys, Cap()));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct) {
        var owner = currentUser.UserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage keys" });
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        bool ok = await store.RevokeAsync(id, owner.Value, ct);
        if (!ok) return NotFound(new { error = "key not found" });
        return Ok(new { revoked = true });
    }
}
