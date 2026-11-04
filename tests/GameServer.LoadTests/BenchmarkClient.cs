using System.Diagnostics;
using System.Net;
using GameServer.Networking.Configuration;
using System.Net.Sockets;
using System.Text.Json;
using GameServer.Infrastructure.Authentication;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;

namespace GameServer.LoadTests;

public static class BenchmarkClient
{
    private sealed class Client(TcpClient socket, Stream stream, ClientPacketTransport transport) : IAsyncDisposable
    {
        public async ValueTask ExchangeAsync(byte[] packet, CancellationToken ct)
        {
            await transport.ExchangeAsync(packet, ct);
            if (transport.Header.MessageId == LoadEchoHandler.MessageId && !transport.Body.Span.SequenceEqual(packet.AsSpan(28)))
                throw new IOException("Echo payload mismatch.");
        }
        public async ValueTask DisposeAsync()
        {
            socket.Dispose();
            transport.Dispose();
            await stream.DisposeAsync();
        }
    }

    private sealed class Latencies
    {
        // Fixed memory: 0.05 ms bins through 1000 ms; the last bin is overflow.
        public readonly long[] Bins = new long[20001];
        public long Count;
        public double MaxMs;
        public void Add(double ms)
        {
            Bins[Math.Min(20000, (int)Math.Ceiling(ms * 20))]++;
            Count++;
            MaxMs = Math.Max(MaxMs, ms);
        }
        public void Merge(Latencies other)
        {
            for (int i = 0; i < Bins.Length; i++) Bins[i] += other.Bins[i];
            Count += other.Count;
            MaxMs = Math.Max(MaxMs, other.MaxMs);
        }
        public double Percentile(double quantile)
        {
            long target = (long)Math.Ceiling(Count * quantile), sum = 0;
            for (int i = 0; i < Bins.Length; i++)
            {
                sum += Bins[i];
                if (sum >= target) return i / 20.0;
            }
            return 1000;
        }
    }

    private static void Emit(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
    private static byte[] Encode(int messageId, IMessage? message)
    {
        byte[] packet = new byte[28 + (message?.CalculateSize() ?? 0)];
        PacketCodec.WriteMessage(packet, messageId, 42, ResultCode.Ok, message);
        return packet;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i += 2) options.Add(args[i], args[i + 1]);
        int count = int.Parse(options.GetValueOrDefault("--count", "8000"));
        int port = int.Parse(options["--port"]);
        int workers = int.Parse(options.GetValueOrDefault("--workers", "128"));
        int duration = int.Parse(options.GetValueOrDefault("--duration", "20"));
        int idle = int.Parse(options.GetValueOrDefault("--idle", "15"));
        if (count is < 1 or > 8000 || workers < 1 || workers > count || duration < 1 || idle < 1)
            throw new ArgumentOutOfRangeException(nameof(args));
        var encryption = Enum.Parse<PacketEncryptionMode>(options.GetValueOrDefault("--encryption", "AesGcm"), ignoreCase: true);
        var verifier = new JwtLoginTokenVerifier(new()
        {
            SigningKeyBase64 = Environment.GetEnvironmentVariable("GAMESERVER_Authentication__SigningKeyBase64") ?? throw new InvalidOperationException("Missing TEST signing key.")
        });
        var clients = new Client[count];
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            Emit(new { @event = "phase_start", phase = "login" });
            var watch = Stopwatch.StartNew();
            using var gate = new SemaphoreSlim(64);
            var connecting = new List<Task>();
            async Task ConnectAsync(int index)
            {
                await gate.WaitAsync(lifetime.Token);
                TcpClient? socket = null;
                Stream? stream = null;
                try
                {
                    socket = new TcpClient { NoDelay = true };
                    await socket.ConnectAsync(IPAddress.Loopback, port, lifetime.Token);
                    stream = socket.GetStream();
                    var transport = await ClientPacketTransport.ConnectAsync(stream, encryption, lifetime.Token);
                    var client = new Client(socket, stream, transport);
                    int id = index + 1;
                    await client.ExchangeAsync(Encode((int)MessageId.Login, new LoginRequest
                    { DeviceId = $"tcp-load-{id}", AccessToken = verifier.Issue(id, $"tcp-load-{id}", 1800) }), lifetime.Token);
                    clients[index] = client;
                }
                catch
                {
                    socket?.Dispose();
                    if (stream != null) await stream.DisposeAsync();
                    throw;
                }
                finally { gate.Release(); }
            }
            for (int i = 0; i < count; i++)
            {
                connecting.Add(ConnectAsync(i));
                if (i % 20 == 19) await Task.Delay(TimeSpan.FromSeconds(20.0 / 700), lifetime.Token);
            }
            await Task.WhenAll(connecting);
            Emit(new { @event = "phase_end", phase = "login", connections = count, seconds = watch.Elapsed.TotalSeconds });
            byte[] heartbeat = Encode((int)MessageId.Heartbeat, null);
            byte[] echo = Encode(LoadEchoHandler.MessageId, new SaveWriteRequest { Data = ByteString.CopyFrom(new byte[1024]) });

            async Task CommunicateAsync(string phase, byte[] packet, int seconds)
            {
                Emit(new { @event = "phase_start", phase });
                long deadline = Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency;
                watch.Restart();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(seconds + 15));
                async Task<Latencies> WorkAsync(int worker)
                {
                    var latencies = new Latencies();
                    // Disjoint partitions: no two workers ever use one stream.
                    int index = worker;
                    while (Stopwatch.GetTimestamp() < deadline)
                    {
                        long start = Stopwatch.GetTimestamp();
                        await clients[index].ExchangeAsync(packet, timeout.Token);
                        latencies.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                        index += workers;
                        if (index >= count) index = worker;
                    }
                    return latencies;
                }
                var parts = await Task.WhenAll(Enumerable.Range(0, workers).Select(WorkAsync));
                var total = new Latencies();
                foreach (var part in parts) total.Merge(part);
                double elapsed = watch.Elapsed.TotalSeconds;
                Emit(new { @event = "phase_end", phase, requests = total.Count, seconds = elapsed,
                    requests_per_second = total.Count / elapsed, p50_ms = total.Percentile(.5), p95_ms = total.Percentile(.95),
                    p99_ms = total.Percentile(.99), max_ms = total.MaxMs, payload_bytes = packet.Length - 28, histogram_bin_ms = .05 });
            }
            async Task IdleAsync(string phase, int seconds)
            {
                Emit(new { @event = "phase_start", phase });
                watch.Restart();
                await Task.Delay(TimeSpan.FromSeconds(seconds), lifetime.Token);
                Emit(new { @event = "phase_end", phase, seconds = watch.Elapsed.TotalSeconds, connections = count });
            }
            await CommunicateAsync("warmup_heartbeat", heartbeat, 3);
            await CommunicateAsync("warmup_echo", echo, 3);
            await IdleAsync("idle", idle);
            await CommunicateAsync("heartbeat", heartbeat, duration);
            await CommunicateAsync("protobuf_1k", echo, duration);
            await IdleAsync("idle_after_traffic", 10);
            Emit(new { @event = "complete", connections = count, failures = 0 });
            return 0;
        }
        catch (Exception ex)
        {
            Emit(new { @event = "failure", exception_type = ex.GetType().Name });
            return 1;
        }
        finally
        {
            await Task.WhenAll(clients.Where(c => c != null).Select(async client => await client.DisposeAsync()));
        }
    }
}
