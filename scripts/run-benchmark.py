#!/usr/bin/env python3
"""Standalone TCP CPU/memory comparison: plain versus optional AES-GCM.
CPU 100% means one fully occupied core; process CPU time includes all threads.
A fixed client CPU affinity keeps load generation separate from four server CPUs.
The sampled 4 GiB RSS guard is not a real cgroup memory limit.
"""
import argparse
import base64
import csv
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import shutil
import socket
import subprocess
import threading
import time

ROOT = Path(__file__).resolve().parents[1]


def process_stats(pid):
    folder = Path(f'/proc/{pid}')
    status = dict(line.split(':', 1) for line in (folder / 'status').read_text().splitlines())
    fields = (folder / 'stat').read_text().rsplit(')', 1)[1].split()
    return {
        'rss_bytes': int(status.get('VmRSS', '0 kB').split()[0]) * 1024,
        'cpu_seconds': (int(fields[11]) + int(fields[12])) / os.sysconf('SC_CLK_TCK'),
        'threads': int(status['Threads']),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--zero-read', choices=['false', 'true'], default='true')
    parser.add_argument('--encryption', choices=['none', 'aes-gcm'], default='none')
    parser.add_argument('--count', type=int, default=8000)
    parser.add_argument('--duration', type=int, default=20)
    parser.add_argument('--idle', type=int, default=15)
    parser.add_argument('--workers', type=int, default=128)
    parser.add_argument('--port', type=int, default=22370)
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    sdk_dir = os.environ.get('DOTNET_ROOT') or os.environ.get('TCP_DOTNET_ROOT')
    dotnet_path = str(Path(sdk_dir) / 'dotnet') if sdk_dir else shutil.which('dotnet')
    if not dotnet_path or not Path(dotnet_path).is_file():
        raise RuntimeError('Set DOTNET_ROOT or add the .NET SDK to PATH')
    dotnet = Path(dotnet_path)
    host = ROOT / 'tests/GameServer.LoadTests/bin/Release/net10.0/GameServer.LoadTests.dll'
    if not host.exists():
        raise RuntimeError('Build Release first')
    allowed_cpus = sorted(os.sched_getaffinity(0))
    if len(allowed_cpus) < 5:
        raise RuntimeError('This comparison requires four server CPUs plus one client CPU')
    server_cpus, client_cpus = allowed_cpus[:4], allowed_cpus[4:5]
    common = os.environ.copy()
    if any(common.get(key) for key in ['LD_PRELOAD', 'MALLOC_CONF', 'MALLOC_ARENA_MAX', 'MALLOC_MMAP_THRESHOLD_']):
        raise RuntimeError('Run A/B from a process without inherited allocator overrides; use a clean process for controlled TCP comparisons')
    # Only a fresh local test credential is issued; no key is written to disk.
    mode = 'AesGcm' if args.encryption == 'aes-gcm' else 'None'
    common.update({
        'GAMESERVER_Networking__Encryption__Mode': mode,
        'GAMESERVER_Networking__Encryption__KeyBase64': base64.b64encode(os.urandom(32)).decode(),
        'GAMESERVER_Authentication__SigningKeyBase64': base64.b64encode(os.urandom(32)).decode(),
        'GAMESERVER_Database__ConnectionString': 'Server=127.0.0.1;Port=13307;Database=gameserver_tcp_test;User ID=gameserver_tcp_test;SslMode=Disabled',
        'GAMESERVER_Networking__ListenAddress': '127.0.0.1',
        'GAMESERVER_Networking__Port': str(args.port),
        'GAMESERVER_Networking__UseZeroByteRead': args.zero_read,
        'GAMESERVER_Storage__RootPath': str(out / 'saves'),
    })
    server_env = common | {'DOTNET_PROCESSOR_COUNT': '4', 'DOTNET_GCHeapHardLimit': 'C0000000'}
    client_env = common | {'DOTNET_PROCESSOR_COUNT': str(len(client_cpus))}
    metadata = {
        'zero_read': args.zero_read == 'true', 'encryption': mode, 'tls': False, 'allocator': 'system',
        'per_packet_encryption_bytes': 16 if mode == 'AesGcm' else 0, 'initialization_bytes': 40 if mode == 'AesGcm' else 0,
        'count': args.count, 'workers': min(args.workers, args.count), 'duration_seconds': args.duration, 'idle_seconds': args.idle,
        'server_cpus': server_cpus, 'client_cpus': client_cpus, 'cpu_100_percent_means': 'one fully occupied core',
        'true_4gib_memory_limit': False, 'gc_heap_limit_bytes': 3 * 1024 ** 3, 'rss_watchdog_bytes': 4 * 1024 ** 3,
        'echo_host': 'test executable --serve only, production has no echo route, real JWT and MySQL login',
        'assemblies_sha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(host.parent.glob('*.dll'))},
    }
    (out / 'configuration.json').write_text(json.dumps(metadata, indent=2) + '\n')
    phase = ['startup']
    runtime = {}
    runtime_at = [0.0]
    samples, results, violations = [], [], []
    client_pid = [None]
    done = threading.Event()
    server_log = (out / 'server.log').open('w')
    server = subprocess.Popen([str(dotnet), str(host), '--serve'], cwd=host.parent, env=server_env,
                              stdout=server_log, stderr=subprocess.STDOUT,
                              preexec_fn=lambda: os.sched_setaffinity(0, server_cpus))
    started = time.monotonic()

    def monitor():
        # Read telemetry incrementally; no per-request logging is added to server.
        with (out / 'server.log').open() as log, (out / 'resources.csv').open('w', newline='') as file:
            columns = ['seconds', 'phase', 'server_rss', 'server_cpu_seconds', 'server_threads', 'client_rss', 'client_cpu_seconds',
                       'managed_estimate', 'heap_last_gc', 'gc_committed', 'receive_in_use', 'send_in_use', 'allocated_total', 'gc_pause_total_ms', 'telemetry_age_seconds']
            writer = csv.DictWriter(file, fieldnames=columns)
            writer.writeheader()
            while not done.wait(.25):
                for line in log.readlines():
                    if 'Resources RSS=' in line:
                        runtime.clear()
                        runtime.update({k: float(v) for k, v in re.findall(r'(\w+)=([\d.]+)', line)})
                        runtime_at[0] = time.monotonic()
                try:
                    stats = process_stats(server.pid)
                    if stats['rss_bytes'] <= 0:
                        continue
                    client = process_stats(client_pid[0]) if client_pid[0] else {}
                except (FileNotFoundError, KeyError, ProcessLookupError):
                    continue
                row = {
                    'seconds': time.monotonic() - started, 'phase': phase[0], 'server_rss': stats['rss_bytes'],
                    'server_cpu_seconds': stats['cpu_seconds'], 'server_threads': stats['threads'],
                    'client_rss': client.get('rss_bytes', 0), 'client_cpu_seconds': client.get('cpu_seconds', 0),
                    'managed_estimate': runtime.get('ManagedBytes'), 'heap_last_gc': runtime.get('Heap'),
                    'gc_committed': runtime.get('Committed'), 'receive_in_use': runtime.get('ReceiveInUse'),
                    'send_in_use': runtime.get('SendInUse'), 'allocated_total': runtime.get('Allocated'),
                    'gc_pause_total_ms': runtime.get('TotalGCPauseMs'),
                    'telemetry_age_seconds': time.monotonic() - runtime_at[0] if runtime else None,
                }
                samples.append(row)
                writer.writerow(row)
                file.flush()
                if stats['rss_bytes'] > 4 * 1024 ** 3:
                    violations.append('Sampled 4 GiB RSS guard exceeded')
                    server.terminate()
                    return

    watcher = threading.Thread(target=monitor, daemon=True)
    watcher.start()
    client = None
    exit_code = None
    try:
        deadline = time.monotonic() + 20
        while True:
            if server.poll() is not None:
                raise RuntimeError('Server startup failed; inspect server.log')
            try:
                with socket.create_connection(('127.0.0.1', args.port), .25):
                    break
            except OSError:
                if time.monotonic() > deadline:
                    raise RuntimeError('Readiness timeout')
                time.sleep(.1)
        command = [str(dotnet), str(host), '--benchmark', '--port', str(args.port), '--count', str(args.count),
                   '--workers', str(min(args.workers, args.count)), '--duration', str(args.duration), '--idle', str(args.idle)]
        command.extend(['--encryption', mode])
        client = subprocess.Popen(command, cwd=host.parent, env=client_env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                  text=True, preexec_fn=lambda: os.sched_setaffinity(0, client_cpus))
        client_pid[0] = client.pid
        beginnings = {}
        with (out / 'client.jsonl').open('w') as events:
            for line in client.stdout:
                events.write(line)
                events.flush()
                event = json.loads(line)
                print(json.dumps(event), flush=True)
                if event['event'] == 'phase_start':
                    phase[0] = event['phase']
                    beginnings[phase[0]] = (time.monotonic(), process_stats(server.pid), process_stats(client.pid))
                elif event['event'] == 'phase_end':
                    begin, old_server, old_client = beginnings[event['phase']]
                    elapsed = time.monotonic() - begin
                    new_server, new_client = process_stats(server.pid), process_stats(client.pid)
                    server_cpu = new_server['cpu_seconds'] - old_server['cpu_seconds']
                    client_cpu = new_client['cpu_seconds'] - old_client['cpu_seconds']
                    event.update({'server_cpu_seconds': server_cpu, 'server_cpu_percent_one_core': server_cpu / elapsed * 100,
                                  'server_cpu_percent_four_cores': server_cpu / elapsed / len(server_cpus) * 100,
                                  'client_cpu_percent_one_core': client_cpu / elapsed * 100,
                                  'server_cpu_us_per_request': server_cpu * 1e6 / event['requests'] if event.get('requests') else None})
                    results.append(event)
        exit_code = client.wait(timeout=20)
        (out / 'client-stderr.log').write_text(client.stderr.read())
        if exit_code:
            raise RuntimeError('Load client failed; inspect client.jsonl and client-stderr.log')
        phase[0] = 'cleanup'
        time.sleep(7)
    finally:
        if client is not None and client.poll() is None:
            client.kill()
            client.wait()
        if server.poll() is None:
            server.send_signal(signal.SIGTERM)
        try:
            server.wait(timeout=20)
        except subprocess.TimeoutExpired:
            server.kill()
            server.wait()
            violations.append('Forced termination')
        done.set()
        watcher.join(timeout=2)
        server_log.close()
        for result in results:
            rows = [r for r in samples if r['phase'] == result['phase']]
            if not rows:
                continue
            recent = [r for r in rows if r['telemetry_age_seconds'] is not None and r['telemetry_age_seconds'] < 6]
            result.update({'peak_server_rss_bytes': max(r['server_rss'] for r in rows),
                           'last_server_rss_bytes': rows[-1]['server_rss'],
                           'peak_client_rss_bytes': max(r['client_rss'] for r in rows),
                           'peak_server_threads': max(r['server_threads'] for r in rows),
                           'last_sampled_managed_estimate_bytes': recent[-1]['managed_estimate'] if recent else None,
                           'last_sampled_heap_after_gc_bytes': recent[-1]['heap_last_gc'] if recent else None,
                           'last_sampled_gc_committed_bytes': recent[-1]['gc_committed'] if recent else None,
                           'last_sampled_receive_in_use_bytes': recent[-1]['receive_in_use'] if recent else None})
        (out / 'summary.json').write_text(json.dumps({'phases': results, 'client_exit_code': exit_code,
                                                     'server_exit_code': server.returncode, 'violations': violations}, indent=2) + '\n')
    if violations:
        raise RuntimeError('; '.join(violations))


if __name__ == '__main__':
    main()
