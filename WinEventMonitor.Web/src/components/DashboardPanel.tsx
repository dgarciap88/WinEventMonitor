import { useState, useEffect, useCallback } from 'react';
import { getStats } from '../api/client';
import type { Stats } from '../api/types';
import { Timestamp } from './Timestamp';

const SEVERITY_COLOR: Record<string, string> = {
  High:   'bg-red-100 text-red-700 border-red-300',
  Medium: 'bg-orange-100 text-orange-700 border-orange-300',
  Low:    'bg-yellow-100 text-yellow-700 border-yellow-300',
};

function KpiCard({ label, total, last24h, color }: { label: string; total: number; last24h: number; color: string }) {
  return (
    <div className={`rounded-lg border p-4 ${color} flex flex-col gap-1`}>
      <span className="text-xs font-semibold uppercase tracking-wide opacity-70">{label}</span>
      <span className="text-3xl font-bold tabular-nums">{total.toLocaleString()}</span>
      <span className="text-xs opacity-80">Últimas 24 h: <strong>{last24h.toLocaleString()}</strong></span>
    </div>
  );
}

function MiniBar({ value, max, color }: { value: number; max: number; color: string }) {
  const pct = max === 0 ? 0 : Math.round((value / max) * 100);
  return (
    <div className="flex items-center gap-2">
      <div className="flex-1 h-2 bg-gray-100 rounded-full overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${pct}%` }} />
      </div>
      <span className="text-xs tabular-nums text-gray-600 w-10 text-right">{value}</span>
    </div>
  );
}

/** Gráfico de barras verticales para actividad por hora (últimas 24 h) */
function ActivityChart({ data }: { data: Stats['activityByHour'] }) {
  const maxVal = Math.max(...data.flatMap(d => [d.processes, d.network]), 1);
  const now = new Date().getUTCHours();

  return (
    <div className="flex items-end gap-0.5 h-24">
      {data.map(d => {
        const hLabel = `${String(d.hour).padStart(2, '0')}:00`;
        const isCurrent = d.hour === now;
        return (
          <div key={d.hour} className="flex-1 flex flex-col items-center gap-0.5 group relative">
            {/* tooltip */}
            <div className="absolute bottom-full mb-1 left-1/2 -translate-x-1/2 z-10 hidden group-hover:flex
                            flex-col bg-gray-800 text-white text-[10px] rounded px-1.5 py-1 whitespace-nowrap shadow">
              <span>{hLabel}</span>
              <span className="text-blue-300">Proc: {d.processes}</span>
              <span className="text-green-300">Red: {d.network}</span>
            </div>
            {/* barras apiladas */}
            <div className="w-full flex flex-col justify-end h-20 gap-px">
              <div
                className={`w-full rounded-t ${isCurrent ? 'bg-blue-500' : 'bg-blue-300'} transition-all`}
                style={{ height: `${Math.round((d.processes / maxVal) * 75)}px` }}
              />
              <div
                className={`w-full ${isCurrent ? 'bg-green-500' : 'bg-green-300'} transition-all`}
                style={{ height: `${Math.round((d.network / maxVal) * 75)}px` }}
              />
            </div>
            {/* etiqueta cada 4 h */}
            {d.hour % 4 === 0 && (
              <span className="text-[8px] text-gray-400 leading-none">{String(d.hour).padStart(2, '0')}</span>
            )}
          </div>
        );
      })}
    </div>
  );
}

export function DashboardPanel() {
  const [stats, setStats] = useState<Stats | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastRefresh, setLastRefresh] = useState<Date | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    getStats()
      .then(s => { setStats(s); setLastRefresh(new Date()); })
      .catch(() => setError('No se pudo conectar con el servicio.'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    load();
    const id = setInterval(load, 60_000);
    return () => clearInterval(id);
  }, [load]);

  if (error) {
    return (
      <div className="mt-6 text-center text-red-600">
        <p>{error}</p>
        <button className="mt-2 text-sm underline text-blue-600" onClick={load}>Reintentar</button>
      </div>
    );
  }

  if (!stats) {
    return <p className="text-gray-400 mt-6 text-center text-sm">{loading ? 'Cargando estadísticas…' : '—'}</p>;
  }

  const maxIp  = Math.max(...stats.topIps.map(x => x.count), 1);
  const maxNetP = Math.max(...stats.topNetProcs.map(x => x.count), 1);
  const maxDns = Math.max(...stats.topDomains.map(x => x.count), 1);

  return (
    <div className="space-y-5">

      {/* Cabecera */}
      <div className="flex items-center justify-between">
        <h2 className="text-base font-semibold text-gray-700">Resumen del sistema</h2>
        <div className="flex items-center gap-3 text-xs text-gray-400">
          {lastRefresh && <span>Actualizado: {lastRefresh.toLocaleTimeString()}</span>}
          <button
            className="border px-2 py-0.5 rounded hover:bg-gray-50 text-gray-500"
            disabled={loading}
            onClick={load}
          >
            {loading ? '…' : '↺ Refrescar'}
          </button>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <KpiCard label="Procesos"       total={stats.totals.totalProcesses} last24h={stats.last24h.proc24}    color="bg-blue-50   border-blue-200   text-blue-900" />
        <KpiCard label="Conexiones red" total={stats.totals.totalNetwork}   last24h={stats.last24h.net24}     color="bg-green-50  border-green-200  text-green-900" />
        <KpiCard label="Consultas DNS"  total={stats.totals.totalDns}       last24h={stats.last24h.dns24}     color="bg-purple-50 border-purple-200 text-purple-900" />
        <KpiCard label="Alertas"        total={stats.totals.totalAlerts}    last24h={stats.last24h.alerts24}  color="bg-red-50    border-red-200    text-red-900" />
      </div>

      {/* Actividad por hora + Alertas por severidad (lado a lado) */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">

        {/* Gráfico de actividad */}
        <div className="md:col-span-2 border rounded-lg p-4 bg-white">
          <div className="flex items-center justify-between mb-2">
            <span className="text-sm font-semibold text-gray-600">Actividad por hora (UTC)</span>
            <div className="flex gap-3 text-[10px] text-gray-500">
              <span className="flex items-center gap-1"><span className="inline-block w-2 h-2 rounded-sm bg-blue-400" /> Procesos</span>
              <span className="flex items-center gap-1"><span className="inline-block w-2 h-2 rounded-sm bg-green-400" /> Red</span>
            </div>
          </div>
          <ActivityChart data={stats.activityByHour} />
        </div>

        {/* Alertas por severidad */}
        <div className="border rounded-lg p-4 bg-white">
          <span className="text-sm font-semibold text-gray-600 block mb-3">Alertas por severidad</span>
          {stats.alertsBySeverity.length === 0
            ? <p className="text-xs text-gray-400">Sin alertas registradas.</p>
            : (
              <div className="space-y-2">
                {(['High', 'Medium', 'Low'] as const).map(sev => {
                  const item = stats.alertsBySeverity.find(x => x.severity === sev);
                  return (
                    <div key={sev} className="flex items-center justify-between">
                      <span className={`text-xs font-semibold px-2 py-0.5 rounded border ${SEVERITY_COLOR[sev]}`}>{sev}</span>
                      <span className="text-lg font-bold tabular-nums text-gray-700">{item?.count ?? 0}</span>
                    </div>
                  );
                })}
              </div>
            )
          }
        </div>
      </div>

      {/* Top tablas: IPs, Procesos red, DNS */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">

        {/* Top IPs destino */}
        <div className="border rounded-lg p-4 bg-white">
          <span className="text-sm font-semibold text-gray-600 block mb-3">Top IPs destino (24 h)</span>
          {stats.topIps.length === 0
            ? <p className="text-xs text-gray-400">Sin datos.</p>
            : (
              <div className="space-y-2">
                {stats.topIps.map(x => (
                  <div key={x.ip}>
                    <div className="flex justify-between text-xs mb-0.5">
                      <span className="font-mono text-gray-700 truncate max-w-[160px]" title={x.ip}>{x.ip}</span>
                    </div>
                    <MiniBar value={x.count} max={maxIp} color="bg-blue-400" />
                  </div>
                ))}
              </div>
            )
          }
        </div>

        {/* Top procesos por red */}
        <div className="border rounded-lg p-4 bg-white">
          <span className="text-sm font-semibold text-gray-600 block mb-3">Top procesos de red (24 h)</span>
          {stats.topNetProcs.length === 0
            ? <p className="text-xs text-gray-400">Sin datos.</p>
            : (
              <div className="space-y-2">
                {stats.topNetProcs.map(x => (
                  <div key={x.process}>
                    <div className="text-xs text-gray-700 mb-0.5 truncate" title={x.process}>{x.process}</div>
                    <MiniBar value={x.count} max={maxNetP} color="bg-green-400" />
                  </div>
                ))}
              </div>
            )
          }
        </div>

        {/* Top dominios DNS */}
        <div className="border rounded-lg p-4 bg-white">
          <span className="text-sm font-semibold text-gray-600 block mb-3">Top dominios DNS (24 h)</span>
          {stats.topDomains.length === 0
            ? <p className="text-xs text-gray-400">Sin datos.</p>
            : (
              <div className="space-y-2">
                {stats.topDomains.map(x => (
                  <div key={x.domain}>
                    <div className="text-xs text-gray-700 mb-0.5 truncate" title={x.domain}>{x.domain}</div>
                    <MiniBar value={x.count} max={maxDns} color="bg-purple-400" />
                  </div>
                ))}
              </div>
            )
          }
        </div>
      </div>

      {/* Alertas recientes */}
      <div className="border rounded-lg p-4 bg-white">
        <span className="text-sm font-semibold text-gray-600 block mb-3">Alertas recientes</span>
        {stats.recentAlerts.length === 0
          ? <p className="text-xs text-gray-400">Sin alertas recientes.</p>
          : (
            <div className="overflow-x-auto">
              <table className="min-w-full text-xs">
                <thead className="text-gray-500 uppercase text-[10px]">
                  <tr>
                    <th className="py-1 pr-3 text-left">Timestamp</th>
                    <th className="py-1 pr-3 text-left">Severidad</th>
                    <th className="py-1 pr-3 text-left">Regla</th>
                    <th className="py-1 pr-3 text-left">Proceso</th>
                    <th className="py-1 text-left">Descripción</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {stats.recentAlerts.map(a => (
                    <tr key={a.id} className="hover:bg-gray-50">
                      <td className="py-1.5 pr-3 whitespace-nowrap"><Timestamp value={a.timestamp} /></td>
                      <td className="py-1.5 pr-3">
                        <span className={`px-1.5 py-0.5 rounded border text-[10px] font-semibold ${SEVERITY_COLOR[a.severity] ?? 'bg-gray-100 text-gray-600'}`}>
                          {a.severity}
                        </span>
                      </td>
                      <td className="py-1.5 pr-3 font-mono">{a.rule}</td>
                      <td className="py-1.5 pr-3 text-gray-600">{a.processName ?? '—'}</td>
                      <td className="py-1.5 text-gray-500 truncate max-w-[300px]" title={a.description}>{a.description}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        }
      </div>

    </div>
  );
}
