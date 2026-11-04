using System.Diagnostics;
using GameServer.Gameplay.Diagnostics;
using GameServer.Gameplay.Sessions;
using GameServer.Networking.Limits;
using GameServer.Networking.Diagnostics;
using GameServer.Networking.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameServer.Host.Hosting;

public sealed class ResourceTelemetryService(BufferPools buffers, NetworkMetrics network, GameplayMetrics gameplay, GameConnectionHandler sessions, ConnectionAdmissionController admission, ILogger<ResourceTelemetryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        long lastIndex = -1;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                using var process = Process.GetCurrentProcess();
                var gc = GC.GetGCMemoryInfo();
                double pauseMs = gc.Index != lastIndex ? gc.PauseDurations.ToArray().Sum(x => x.TotalMilliseconds) : 0;
                lastIndex = gc.Index;
                logger.LogInformation("Resources RSS={Rss} Heap={Heap} Committed={Committed} GCIndex={GcIndex} LastGCPauseMs={PauseMs:F3} TotalGCPauseMs={TotalPauseMs:F3} Connections={Connections} ReceiveInUse={Receive} SendInUse={Send} Retained={Retained} Allocated={Allocated} Sessions={Sessions} Players={Players} Unauthenticated={Unauthenticated} Queued={Queued} Running={Running} PendingSend={PendingSend} Accepted={Accepted} Rejected={Rejected} ManagedBytes={ManagedBytes} CpuSeconds={CpuSeconds:F3}",
                    process.WorkingSet64, gc.HeapSizeBytes, gc.TotalCommittedBytes, gc.Index, pauseMs, GC.GetTotalPauseDuration().TotalMilliseconds,
                    network.Active, buffers.Receive.InUseBytes, buffers.Send.InUseBytes, buffers.Receive.RetainedBytes + buffers.Send.RetainedBytes, GC.GetTotalAllocatedBytes(), gameplay.Sessions, gameplay.Players, sessions.UnauthenticatedCount, gameplay.Queued, gameplay.Running, admission.PendingSendBytes.Used, network.AcceptedCount, network.RejectedCount, GC.GetTotalMemory(forceFullCollection: false), process.TotalProcessorTime.TotalSeconds);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
