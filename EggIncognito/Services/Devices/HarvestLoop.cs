namespace EggIncognito.Services.Devices;

public sealed class HarvestLoop {
    private readonly Lock _gate = new();
    private bool _running;
    private bool _dirty;
    private bool _force;

    public bool Running {
        get {
            lock (_gate) return _running;
        }
    }

    public bool Queued {
        get {
            lock (_gate) return _dirty;
        }
    }

    public Task Idle {
        get {
            lock (_gate) return field;
        }

        private set;
    } = Task.CompletedTask;

    public void Poke(bool force, Func<bool, Task> harvest, Action<Exception> onError) {
        lock (_gate) {
            _force |= force;
            if (_running) {
                _dirty = true;
                return;
            }

            _running = true;
            Idle = Task.Run(() => LoopAsync(harvest, onError));
        }
    }

    private async Task LoopAsync(Func<bool, Task> harvest, Action<Exception> onError) {
        while (true) {
            bool force;
            lock (_gate) {
                _dirty = false;
                force = _force;
                _force = false;
            }

            try {
                await harvest(force);
            } catch (Exception ex) {
                onError(ex);
            }

            lock (_gate) {
                if (_dirty) continue;
                _running = false;
                return;
            }
        }
    }
}
