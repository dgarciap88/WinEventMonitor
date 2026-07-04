import { useState, useEffect, useCallback } from 'react';
import { getSystemHealth, getSystemHistory } from '../api/client';
import type { SystemSnapshot, HistoryPoint } from '../api/types';

// ─── Helpers ───────────────────────────────────────────────────────────────

function fmtBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B/s`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB/s`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB/s`;
}

// ─── Mini barra de progreso ────────────────────────────────────────────────

function Bar({ pct, color = 'bg-blue-500' }: { pct: number; color?: string }) {
  const clamped = Math.min(100, Math.max(0, pct));
  return (
    <div className="w-full bg-gray-200 rounded-full h-2">
      <div
        className={`${color} h-2 rounded-full transition-all duration-500`}
        style={{ width: `${clamped}%` }}
      />
    </div>
  );
}

// ─── Tarjeta KPI ──────────────────────────────────────────────────────────

interface KpiCardProps {
  title: string;
  value: string;
  sub?: string;
  pct: number;
  barColor?: string;
}

function KpiCard({ title, value, sub, pct, barColor }: KpiCardProps) {
  return (
    <div className="bg-white rounded-xl border shadow-sm p-4 flex flex-col gap-2">
      <div className="flex justify-between items-baseline">
        <span className="text-sm text-gray-500 font-medium">{title}</span>
        <span className="text-xl font-bold text-gray-800">{value}</span>
      </div>
      <Bar pct={pct} color={barColor} />
      {sub && <span className="text-xs text-gray-400">{sub}</span>}
    </div>
  );
}

// ─── Sparkline SVG ────────────────────────────────────────────────────────

function Sparkline({ points, color = '#3b82f6' }: { points: number[]; color?: string }) {
  if (points.length < 2) return <span className="text-gray-300 text-xs">—</span>;
  const W = 400, H = 52, pad = 2;
  const max = Math.max(...points, 1);
  const coords = points.map((v, i) => {
    const x = pad + (i / (points.length - 1)) * (W - pad * 2);
    const y = H - pad - (v / max) * (H - pad * 2);
    return `${x.toFixed(1)},${y.toFixed(1)}`;
  });
  const last = points[points.length - 1];
  return (
    <div className="flex items-center gap-3 flex-1 min-w-0">
      <svg width="100%" viewBox={`0 0 ${W} ${H}`} className="overflow-visible flex-1" style={{minWidth: 0}}>
        <polyline
          points={coords.join(' ')}
          fill="none"
          stroke={color}
          strokeWidth="1.5"
          strokeLinejoin="round"
          strokeLinecap="round"
        />
      </svg>
      <span className="text-xs font-mono text-gray-700 w-12 text-right flex-shrink-0">{last.toFixed(1)}%</span>
    </div>
  );
}

// ─── Panel de sparklines ─────────────────────────────────────────────────

function SparklinePanel() {
  const [history, setHistory] = useState<HistoryPoint[]>([]);

  useEffect(() => {
    const load = () => getSystemHistory().then(setHistory).catch(() => {});
    load();
    const id = setInterval(load, 5_000);
    return () => clearInterval(id);
  }, []);

  const cpuPts = history.map(h => h.cpuPct);
  const ramPts = history.map(h => h.ramPct);

  return (
    <div className="bg-white rounded-xl border shadow-sm p-4">
      <span className="text-sm font-semibold text-gray-700 block mb-4">Histórico (2 min)</span>
      <div className="flex flex-col gap-4">
        <div className="flex items-center gap-3">
          <span className="text-xs text-gray-500 w-12 flex-shrink-0">CPU</span>
          <Sparkline points={cpuPts} color="#3b82f6" />
        </div>
        <div className="flex items-center gap-3">
          <span className="text-xs text-gray-500 w-12 flex-shrink-0">RAM</span>
          <Sparkline points={ramPts} color="#10b981" />
        </div>
      </div>
    </div>
  );
}

// ─── Componente principal ─────────────────────────────────────────────────

export function SystemPanel() {
  const [snap, setSnap] = useState<SystemSnapshot | null>(null);
  const [error, setError] = useState('');
  const [lastUpdated, setLastUpdated] = useState('');

  const load = useCallback(() => {
    getSystemHealth()
      .then(s => {
        setSnap(s);
        setError('');
        setLastUpdated(new Date().toLocaleTimeString());
      })
      .catch(() => setError('No se puede conectar con el servicio.'));
  }, []);

  useEffect(() => {
    load();
    const id = setInterval(load, 3_000);
    return () => clearInterval(id);
  }, [load]);

  if (error) {
    return (
      <div className="text-red-600 text-sm mt-4">{error}</div>
    );
  }

  if (!snap) {
    return <div className="text-gray-400 text-sm mt-4">Cargando métricas…</div>;
  }

  const ramPct = snap.ram.totalMb > 0
    ? (snap.ram.usedMb / snap.ram.totalMb) * 100
    : 0;

  const cpuColor = snap.cpu.totalPercent > 80
    ? 'bg-red-500'
    : snap.cpu.totalPercent > 50
      ? 'bg-yellow-500'
      : 'bg-blue-500';

  const ramColor = ramPct > 85
    ? 'bg-red-500'
    : ramPct > 65
      ? 'bg-yellow-500'
      : 'bg-green-500';

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h2 className="text-base font-semibold text-gray-700">Salud del Sistema</h2>
        <span className="text-xs text-gray-400">
          Actualizado: {lastUpdated} · refresco 3 s
        </span>
      </div>

      {/* Sparklines históricos */}
      <SparklinePanel />

      {/* KPI cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
        <KpiCard
          title="CPU"
          value={`${snap.cpu.totalPercent.toFixed(1)} %`}
          sub={`${snap.cpu.coreCount} núcleos`}
          pct={snap.cpu.totalPercent}
          barColor={cpuColor}
        />
        <KpiCard
          title="RAM"
          value={`${(snap.ram.usedMb / 1024).toFixed(1)} / ${(snap.ram.totalMb / 1024).toFixed(1)} GB`}
          sub={`${snap.ram.freeMb.toLocaleString()} MB libres`}
          pct={ramPct}
          barColor={ramColor}
        />
        {snap.disk.map(d => {
          const usedGb = d.totalGb - d.freeGb;
          const diskPct = d.totalGb > 0 ? (usedGb / d.totalGb) * 100 : 0;
          const diskColor = diskPct > 90 ? 'bg-red-500' : diskPct > 75 ? 'bg-yellow-500' : 'bg-indigo-500';
          return (
            <KpiCard
              key={d.name}
              title={`Disco ${d.name.replace('\\', '')}`}
              value={`${usedGb.toFixed(1)} / ${d.totalGb.toFixed(1)} GB`}
              sub={`${d.freeGb.toFixed(1)} GB libres`}
              pct={diskPct}
              barColor={diskColor}
            />
          );
        })}
      </div>

      {/* Tabla de procesos top */}
      <div className="bg-white rounded-xl border shadow-sm overflow-hidden">
        <div className="px-4 py-3 border-b flex items-center justify-between">
          <span className="text-sm font-semibold text-gray-700">Top Procesos por CPU</span>
          <span className="text-xs text-gray-400">{snap.processes.length} mostrados</span>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-xs">
            <thead>
              <tr className="bg-gray-50 text-left text-gray-500">
                <th className="px-4 py-2 font-medium w-16">PID</th>
                <th className="px-4 py-2 font-medium">Proceso</th>
                <th className="px-4 py-2 font-medium w-40">CPU %</th>
                <th className="px-4 py-2 font-medium w-40">RAM (MB)</th>
                <th className="px-4 py-2 font-medium w-28 text-right">I/O Lect.</th>
                <th className="px-4 py-2 font-medium w-28 text-right">I/O Escr.</th>
              </tr>
            </thead>
            <tbody>
              {snap.processes.map((p, i) => {
                const rowCpuColor = p.cpuPercent > 50
                  ? 'bg-red-400'
                  : p.cpuPercent > 20
                    ? 'bg-yellow-400'
                    : 'bg-blue-400';
                return (
                  <tr
                    key={`${p.pid}-${i}`}
                    className="border-t hover:bg-gray-50 transition-colors"
                  >
                    <td className="px-4 py-1.5 text-gray-500 font-mono">{p.pid}</td>
                    <td className="px-4 py-1.5 font-medium text-gray-800 truncate max-w-[200px]">
                      {p.name}
                    </td>
                    <td className="px-4 py-1.5">
                      <div className="flex items-center gap-2">
                        <div className="flex-1 bg-gray-100 rounded-full h-1.5">
                          <div
                            className={`${rowCpuColor} h-1.5 rounded-full`}
                            style={{ width: `${Math.min(100, p.cpuPercent)}%` }}
                          />
                        </div>
                        <span className="w-10 text-right text-gray-600">
                          {p.cpuPercent.toFixed(1)}%
                        </span>
                      </div>
                    </td>
                    <td className="px-4 py-1.5">
                      <div className="flex items-center gap-2">
                        <div className="flex-1 bg-gray-100 rounded-full h-1.5">
                          <div
                            className="bg-green-400 h-1.5 rounded-full"
                            style={{ width: `${Math.min(100, (p.workingSetMb / Math.max(...snap.processes.map(x => x.workingSetMb), 1)) * 100)}%` }}
                          />
                        </div>
                        <span className="w-16 text-right text-gray-600">
                          {p.workingSetMb.toLocaleString()} MB
                        </span>
                      </div>
                    </td>
                    <td className="px-4 py-1.5 text-right font-mono text-gray-600">
                      {p.ioReadBytesSec > 0 ? fmtBytes(p.ioReadBytesSec) : '—'}
                    </td>
                    <td className="px-4 py-1.5 text-right font-mono text-gray-600">
                      {p.ioWriteBytesSec > 0 ? fmtBytes(p.ioWriteBytesSec) : '—'}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
