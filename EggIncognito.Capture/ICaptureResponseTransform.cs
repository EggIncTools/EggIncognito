namespace EggIncognito.Capture;

public sealed record CaptureUpstreamResponse(int StatusCode, string? ContentType, byte[] Body);

public interface ICaptureResponseTransform {
    ValueTask<byte[]?> TransformAsync(
        CaptureOverrideRequest request, CaptureUpstreamResponse response, CancellationToken ct);
}
