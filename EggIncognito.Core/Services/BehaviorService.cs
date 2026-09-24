using System.Text;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Core.Services;

public sealed class BehaviorService : IBehaviorService {
    private readonly IReadOnlyList<SimulationBehavior> _behaviors;

    public BehaviorService() : this(DefaultCatalog()) {
    }

    internal BehaviorService(IEnumerable<SimulationBehavior> behaviors) {
        _behaviors = behaviors.ToList();
    }

    public SimulationBehavior? Find(string name) =>
        _behaviors.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<SimulationBehavior> All() => _behaviors;

    public IReadOnlyList<SimulationBehavior> ForEndpoint(string slug) =>
        _behaviors.Where(b => b.Endpoints is null || b.Endpoints.Contains(slug)).ToList();

    private static IEnumerable<SimulationBehavior> DefaultCatalog() => [
        new("server_error", "Generic server failure", 500),
        new("maintenance", "Service temporarily unavailable", 503),
        new("not_found", "Endpoint not found", 404),
        new("unauthorized", "Authentication failure", 401),
        new(
            "rate_limited",
            "Rate limit exceeded",
            429,
            ExtraHeaders: new Dictionary<string, string> { ["Retry-After"] = "60" }),
        new(
            "empty",
            "Valid AuthenticatedMessage wrapper, zero-value inner proto",
            200,
            () => Encoding.UTF8.GetBytes(
                Convert.ToBase64String(
                    new AuthenticatedMessage { Message = ByteString.Empty }.ToByteArray()))),
        new(
            "corrupt",
            "Malformed base64 body (tests parse error handling)",
            200,
            () => "!!corrupt!!"u8.ToArray())
    ];
}
