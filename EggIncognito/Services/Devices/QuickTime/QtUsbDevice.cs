using System.Buffers.Binary;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;

namespace EggIncognito.Services.Devices.QuickTime;

internal sealed class QtUsbDevice : IDisposable {
    private const int AppleVendorId = 0x05AC;
    private const byte QuickTimeSubClass = 0x2A;
    private const byte TransferTypeMask = 0x03;
    private const byte BulkTransferType = 0x02;
    private const int ReEnumerateDelayMs = 500;
    private const int ReEnumerateAttempts = 10;
    private const int ReadTimeoutMs = 2000;
    private const int WriteTimeoutMs = 2000;
    private const int ReadBufferSize = 256 * 1024;

    private readonly UsbContext _ctx = new();
    private IUsbDevice? _device;
    private UsbEndpointReader? _reader;
    private UsbEndpointWriter? _writer;
    private int _interfaceNumber = -1;

    public static async Task<QtUsbDevice?> OpenAsync(string udid, CancellationToken ct) {
        var dev = new QtUsbDevice();
        try {
            if (!await dev.ConnectAsync(udid, ct)) {
                dev.Dispose();
                return null;
            }
            return dev;
        } catch {
            dev.Dispose();
            throw;
        }
    }

    private async Task<bool> ConnectAsync(string udid, CancellationToken ct) {
        if (!TryFind(udid, out var device) || device is null) return false;

        if (!HasQuickTimeConfig(device)) {
            EnableQuickTimeConfig(device);
            device.Dispose();
            device = null;
            for (int i = 0; i < ReEnumerateAttempts; i++) {
                await Task.Delay(ReEnumerateDelayMs, ct);
                if (TryFind(udid, out var found) && found is not null) {
                    if (HasQuickTimeConfig(found)) {
                        device = found;
                        break;
                    }
                    found.Dispose();
                }
            }
            if (device is null) return false;
        }

        _device = device;
        return ClaimQuickTimeInterface();
    }

    private bool TryFind(string udid, out IUsbDevice? found) {
        found = null;
        foreach (var d in _ctx.List()) {
            if (d.VendorId != AppleVendorId) continue;
            if (!d.TryOpen()) continue;
            string? serial = d.Info?.SerialNumber;
            if (!string.IsNullOrEmpty(serial) &&
                !string.Equals(serial, udid, StringComparison.OrdinalIgnoreCase)) {
                d.Dispose();
                continue;
            }
            found = d;
            return true;
        }
        return false;
    }

    private static bool HasQuickTimeConfig(IUsbDevice device) => FindQuickTimeConfig(device) is not null;

    private static (int Config, int Interface, byte ReadEndpoint, byte WriteEndpoint)? FindQuickTimeConfig(
        IUsbDevice device) {
        for (int c = 0; c < device.Configs.Count; c++) {
            var config = device.Configs[c];
            foreach (var iface in config.Interfaces) {
                if (iface.SubClass != QuickTimeSubClass) continue;
                byte read = 0, write = 0;
                foreach (var ep in iface.Endpoints) {
                    if ((ep.Attributes & TransferTypeMask) != BulkTransferType) continue;
                    if ((ep.EndpointAddress & 0x80) != 0) read = ep.EndpointAddress;
                    else write = ep.EndpointAddress;
                }
                if (read != 0 && write != 0)
                    return (config.ConfigurationValue, iface.Number, read, write);
            }
        }
        return null;
    }

    private static void EnableQuickTimeConfig(IUsbDevice device) {
        var setup = new UsbSetupPacket(0x40, 0x52, 0x00, 0x02, 0);
        device.ControlTransfer(setup);
    }

    private static void DisableQuickTimeConfig(IUsbDevice device) {
        var setup = new UsbSetupPacket(0x40, 0x52, 0x00, 0x00, 0);
        device.ControlTransfer(setup);
    }

    private bool ClaimQuickTimeInterface() {
        if (_device is null) return false;
        if (FindQuickTimeConfig(_device) is not { } qt) return false;

        _device.SetConfiguration(qt.Config);
        if (!_device.ClaimInterface(qt.Interface)) return false;
        _interfaceNumber = qt.Interface;

        ClearFeature(qt.ReadEndpoint);
        ClearFeature(qt.WriteEndpoint);

        _reader = _device.OpenEndpointReader((ReadEndpointID)qt.ReadEndpoint, ReadBufferSize);
        _writer = _device.OpenEndpointWriter((WriteEndpointID)qt.WriteEndpoint);
        return _reader is not null && _writer is not null;
    }

    private void ClearFeature(byte endpoint) {
        var setup = new UsbSetupPacket(0x02, 0x01, 0, endpoint, 0);
        _device?.ControlTransfer(setup);
    }

    public Task SendAsync(byte[] data, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        if (_writer is null) throw new InvalidOperationException("quicktime endpoint writer is not open");
        var code = _writer.Write(data, WriteTimeoutMs, out int written);
        if (code is not Error.Success || written != data.Length)
            throw new IOException($"quicktime usb write failed: {code}");
        return Task.CompletedTask;
    }

    public byte[]? ReadPacket(CancellationToken ct) {
        if (_reader is null) return null;
        byte[] header = new byte[4];
        if (!ReadExact(header, ct)) return null;
        uint total = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (total is < 4 or > ReadBufferSize) return null;
        byte[] body = new byte[total - 4];
        if (body.Length == 0) return body;
        return ReadExact(body, ct) ? body : null;
    }

    private bool ReadExact(byte[] buffer, CancellationToken ct) {
        int at = 0;
        while (at < buffer.Length) {
            ct.ThrowIfCancellationRequested();
            byte[] chunk = new byte[buffer.Length - at];
            var code = _reader!.Read(chunk, ReadTimeoutMs, out int read);
            if (code is Error.Timeout) continue;
            if (code is not Error.Success || read == 0) return false;
            Array.Copy(chunk, 0, buffer, at, read);
            at += read;
        }
        return true;
    }

    public void Dispose() {
        try {
            if (_device is not null) {
                if (_interfaceNumber >= 0) _device.ReleaseInterface(_interfaceNumber);
                DisableQuickTimeConfig(_device);
            }
        } catch (Exception ex) when (ex is UsbException or IOException or InvalidOperationException) {
        }
        _device?.Dispose();
        _ctx.Dispose();
    }
}
