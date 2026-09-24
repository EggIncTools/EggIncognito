using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Runner.Data;

public sealed class RunnerDb(string connectionString) {
    public string ConnectionString { get; } = connectionString;

    public static RunnerDb? FromEnv(Func<string, string> env) {
        var conn = env("ConnectionStrings__Postgres");
        return string.IsNullOrWhiteSpace(conn) ? null : new RunnerDb(conn);
    }

    public EggIncognitoDbContext NewContext() {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new EggIncognitoDbContext(options);
    }
}
