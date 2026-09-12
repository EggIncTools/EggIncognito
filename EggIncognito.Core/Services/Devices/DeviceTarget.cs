using System.Diagnostics.CodeAnalysis;

namespace EggIncognito.Core.Services.Devices;

public sealed record DeviceTarget(string Id, string Platform, string Target, string Package);

public enum DeviceOutcome {
    Ok,
    Unsupported,
    Unreachable,
    Error
}

public readonly record struct DeviceResult(DeviceOutcome Outcome, string? Note) {
    public bool Ok => Outcome == DeviceOutcome.Ok;

    public static DeviceResult Success(string? note = null) => new(DeviceOutcome.Ok, note);
    public static DeviceResult Unsupported(string? note = null) => new(DeviceOutcome.Unsupported, note);
    public static DeviceResult Unreachable(string? note = null) => new(DeviceOutcome.Unreachable, note);
    public static DeviceResult Error(string? note = null) => new(DeviceOutcome.Error, note);

    public static DeviceResult From((bool Ok, string? Note) tuple) =>
        tuple.Ok ? Success(tuple.Note) : Error(tuple.Note);
}

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
public readonly record struct DeviceResult<T>(DeviceOutcome Outcome, T? Value, string? Note) {
    public bool Ok => Outcome == DeviceOutcome.Ok;

    public static DeviceResult<T> Success(T value, string? note = null) => new(DeviceOutcome.Ok, value, note);
    public static DeviceResult<T> Unsupported(string? note = null) => new(DeviceOutcome.Unsupported, default, note);
    public static DeviceResult<T> Unreachable(string? note = null) => new(DeviceOutcome.Unreachable, default, note);
    public static DeviceResult<T> Error(string? note = null) => new(DeviceOutcome.Error, default, note);
}

[Flags]
public enum DeviceCapabilities {
    None = 0,
    BinaryPull = 1 << 0,
    AssetRead = 1 << 1,
    Probe = 1 << 2,
    StoreUpdate = 1 << 3,
    Proxy = 1 << 4,
    CaInstall = 1 << 5,
    AppLifecycle = 1 << 6,
    ParticleCapture = 1 << 7,
    UiNavigation = 1 << 8,
    ScreenStream = 1 << 9
}
