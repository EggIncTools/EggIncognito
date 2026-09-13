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
    private const int MaxPacketSize = 8 * 1024 * 1024;

    private readonly UsbContext _ctx = new();
    private byte[] _buffer = new byte[ReadBufferSize];
    private int _pending;
    private IUsbDevice? _device;
    private UsbEndpointReader? _reader;
    private UsbEndpointWriter? _writer;
    private int _interfaceNumber = -1;

    public static async Task<(QtUsbDevice? Device, string? Note)> OpenAsync(string udid, CancellationToken ct) {
        var dev = new QtUsbDevice();
        try {
            string? note = await dev.ConnectAsync(udid, ct);
            if (note is null) return (dev, null);
            dev.Dispose();
            return (null, note);
        } catch {
            dev.Dispose();
            throw;
        }
    }

    private async Task<string?> ConnectAsync(string udid, CancellationToken ct) {
        if (!TryFind(udid, out var device) || device is null) return "device not present on usb";

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
            if (device is null) return "device did not expose the quicktime config after enabling it";
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

    private string? ClaimQuickTimeInterface() {
        if (_device is null) return "usb device was not opened";
        if (FindQuickTimeConfig(_device) is not { } qt) return "no quicktime av interface on this device";

        bool switched = TrySetConfiguration(qt.Config);
        if (!_device.ClaimInterface(qt.Interface))
            return switched
                ? "could not claim the quicktime av interface"
                : $"device is not on usb configuration {qt.Config} and it could not be switched while " +
                  "another process holds an interface; switch it once on the host " +
                  "(stop usbmuxd, write the configuration, start usbmuxd)";
        _interfaceNumber = qt.Interface;

        ClearFeature(qt.ReadEndpoint);
        ClearFeature(qt.WriteEndpoint);

        _reader = _device.OpenEndpointReader((ReadEndpointID)qt.ReadEndpoint, ReadBufferSize);
        _writer = _device.OpenEndpointWriter((WriteEndpointID)qt.WriteEndpoint);
        return _reader is not null && _writer is not null ? null : "could not open the quicktime bulk endpoints";
    }

    private bool TrySetConfiguration(int config) {
        try {
            _device!.SetConfiguration(config);
            return true;
        } catch (UsbException) {
            return false;
        }
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

        while (true) {
            ct.ThrowIfCancellationRequested();
            if (TryTakePacket() is { } packet) return packet;
            if (!Fill()) return null;
        }
    }

    private byte[]? TryTakePacket() {
        if (_pending < 4) return null;
        uint total = BinaryPrimitives.ReadUInt32LittleEndian(_buffer);
        if (total is < 4 or > MaxPacketSize) {
            _pending = 0;
            return null;
        }
        if (_pending < total) return null;

        byte[] body = _buffer.AsSpan(4, (int)total - 4).ToArray();
        int rest = _pending - (int)total;
        if (rest > 0) Array.Copy(_buffer, (int)total, _buffer, 0, rest);
        _pending = rest;
        return body;
    }

    private bool Fill() {
        if (_pending >= _buffer.Length) Grow();
        byte[] chunk = new byte[_buffer.Length - _pending];
        var code = _reader!.Read(chunk, ReadTimeoutMs, out int read);
        if (code is Error.Timeout) return true;
        if (code is not Error.Success || read == 0) return false;
        Array.Copy(chunk, 0, _buffer, _pending, read);
        _pending += read;
        return true;
    }

    private void Grow() {
        if (_buffer.Length >= MaxPacketSize)
            throw new IOException("quicktime packet exceeded the maximum buffer size");
        byte[] bigger = new byte[Math.Min(_buffer.Length * 2, MaxPacketSize)];
        Array.Copy(_buffer, bigger, _pending);
        _buffer = bigger;
    }

    public void Dispose() {
        try {
            if (_device is not null && _interfaceNumber >= 0) _device.ReleaseInterface(_interfaceNumber);
        } catch (Exception ex) when (ex is UsbException or IOException or InvalidOperationException) {
        }
        _device?.Dispose();
        _ctx.Dispose();
    }
}
