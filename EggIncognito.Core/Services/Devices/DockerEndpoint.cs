namespace EggIncognito.Core.Services.Devices;

public abstract record DockerEndpoint {
    public abstract string Describe();

    public abstract bool Available { get; }

    public sealed record Unix(string SocketPath) : DockerEndpoint {
        public override string Describe() => $"docker socket {SocketPath}";

        public override bool Available => !OperatingSystem.IsWindows() && File.Exists(SocketPath);
    }

    public sealed record Bridge(string BaseUrl, string? Secret) : DockerEndpoint {
        public override string Describe() => $"docker bridge at {BaseUrl}";

        public override bool Available => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrEmpty(Secret);

        public string Root => BridgeRoutes.Absolute(BaseUrl, BridgeRoutes.Docker) + "/";
    }
}
