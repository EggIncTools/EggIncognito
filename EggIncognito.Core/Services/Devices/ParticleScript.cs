namespace EggIncognito.Core.Services.Devices;

public static class ParticleScript {
    public static async Task<string?> BuildStagedAsync(string scriptBody, string? addrOffset, CancellationToken ct) {
        try {
            string prefix = string.IsNullOrWhiteSpace(addrOffset)
                ? ""
                : $"const addrOffset = '{addrOffset.Trim()}';\n";
            string staged = DeviceShell.NewTempPath(".js");
            await File.WriteAllTextAsync(staged, prefix + scriptBody, ct);
            return staged;
        } catch {
            return null;
        }
    }
}
