using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using Microsoft.Extensions.Options;

namespace IndustrialTelemetry.Infrastructure.Channels;

public class TelemetryChannel : ITelemetryChannel
{
    private readonly Channel<TelemetryMeasurement> _channel;
    private readonly int _capacity;
    private long _totalIngested;
    private long _totalDrained;
    private long _totalDropped;

    public int Capacity => _capacity;
    public int CurrentCount => _channel.Reader.Count;

    public TelemetryChannel(IOptions<TelemetryChannelOptions>? options = null)
        : this(options?.Value?.Capacity ?? 50000)
    {
    }

    public TelemetryChannel(int capacity)
    {
        _capacity = capacity > 0 ? capacity : 50000;
        var channelOptions = new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<TelemetryMeasurement>(channelOptions);
    }

    public async ValueTask<bool> WriteAsync(TelemetryMeasurement measurement, CancellationToken ct = default)
    {
        try
        {
            await _channel.Writer.WriteAsync(measurement, ct);
            Interlocked.Increment(ref _totalIngested);
            return true;
        }
        catch (ChannelClosedException)
        {
            Interlocked.Increment(ref _totalDropped);
            return false;
        }
    }

    public async ValueTask<int> WriteBatchAsync(IEnumerable<TelemetryMeasurement> measurements, CancellationToken ct = default)
    {
        int written = 0;
        foreach (var m in measurements)
        {
            if (await WriteAsync(m, ct))
            {
                written++;
            }
        }
        return written;
    }

    public bool TryWrite(TelemetryMeasurement measurement)
    {
        if (_channel.Writer.TryWrite(measurement))
        {
            Interlocked.Increment(ref _totalIngested);
            return true;
        }

        Interlocked.Increment(ref _totalDropped);
        return false;
    }

    public bool TryRead([NotNullWhen(true)] out TelemetryMeasurement? measurement)
    {
        if (_channel.Reader.TryRead(out measurement))
        {
            Interlocked.Increment(ref _totalDrained);
            return true;
        }

        measurement = null;
        return false;
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken ct = default)
    {
        return _channel.Reader.WaitToReadAsync(ct);
    }

    public async IAsyncEnumerable<TelemetryMeasurement> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(ct))
        {
            Interlocked.Increment(ref _totalDrained);
            yield return item;
        }
    }

    public ChannelStatusDto GetStatus()
    {
        return new ChannelStatusDto(
            CurrentCount: _channel.Reader.Count,
            Capacity: _capacity,
            TotalIngested: Interlocked.Read(ref _totalIngested),
            TotalDrained: Interlocked.Read(ref _totalDrained),
            TotalDropped: Interlocked.Read(ref _totalDropped),
            IsWriterCompleted: _channel.Reader.Completion.IsCompleted
        );
    }

    public void CompleteWriter(Exception? error = null)
    {
        _channel.Writer.Complete(error);
    }
}
