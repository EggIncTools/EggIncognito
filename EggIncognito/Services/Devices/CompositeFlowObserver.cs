using EggIncognito.Capture;

namespace EggIncognito.Services.Devices;

public sealed class CompositeFlowObserver(IEnumerable<IProcessedFlowObserver> observers) : IProcessedFlowObserver {
    private readonly IProcessedFlowObserver[] _observers = [.. observers];

    public int Count => _observers.Length;

    public void OnFlowProcessed(string deviceId, DashboardFlow flow) {
        foreach (var observer in _observers) observer.OnFlowProcessed(deviceId, flow);
    }
}
