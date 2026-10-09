#if DEBUG
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EggIncognito.Data.Services;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EggIncognitoDbContext> {
    public EggIncognitoDbContext CreateDbContext(string[] args) {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=localhost;Database=eggincognito_designtime")
            .Options;
        return new EggIncognitoDbContext(options);
    }
}
#endif
