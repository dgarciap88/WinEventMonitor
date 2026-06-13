import { useState, useEffect, useCallback } from 'react';
import { getLogons, getLogonSummary, getConnections } from '../api/client';
import type { LogonEvent, LogonSummary, ConnectionsSnapshot } from '../api/types';
import { exportCsv } from '../utils/exportCsv';
import { Timestamp } from './Timestamp';
import { Pagination } from './Pagination';

const PAGE_SIZE = 50;

// ─── Badge de tipo de logon ────────────────────────────────────────────────

function LogonTypeBadge({ type, name }: { type: number; name: string }) {
  const cls =
    type === 10 ? 'bg-red-100 text-red-700' :
    type === 3  ? 'bg-orange-100 text-orange-700' :
    type === 5  ? 'bg-purple-100 text-purple-700' :
    type === 2  ? 'bg-blue-100 text-blue-700' :
                  'bg-gray-100 text-gray-600';
  return (
    <span className={`px-1.5 py-0 rounded text-[10px] font-medium whitespace-nowrap ${cls}`}>
      {name}
    </span>
  );
}

// ─── Tarjeta KPI ──────────────────────────────────────────────────────────

function KpiCard({ label, value, color = 'text-gray-800' }: { label: string; value: string | number; color?: string }) {
  return (
    <div className="bg-white rounded-xl border shadow-sm p-4 flex flex-col gap-1">
      <span className="text-xs text-gray-500">{label}</span>
      <span className={`text-2xl font-bold ${color}`}>{value}</span>
    </div>
  );
}

// ─── Tabla de logons ──────────────────────────────────────────────────────

interface TableProps {
  rows: LogonEvent[];
  total: number;
  page: number;
  onPage: (p: number) => void;
  loading: boolean;
}

function LogonTable({ rows, total, page, onPage, loading }: TableProps) {
  return (
    <>
      <div className="overflow-x-auto rounded-xl border bg-white shadow-sm">
        <table className="w-full text-xs">
          <thead>
            <tr className="bg-gray-50 text-left text-gray-500 border-b">
              <th className="px-3 py-2 font-medium">Timestamp</th>
              <th className="px-3 py-2 font-medium w-16">Res.</th>
              <th className="px-3 py-2 font-medium">Tipo</th>
              <th className="px-3 py-2 font-medium">Usuario</th>
              <th className="px-3 py-2 font-medium">IP Origen</th>
              <th className="px-3 py-2 font-medium">Estación</th>
              <th className="px-3 py-2 font-medium">Auth</th>
              <th className="px-3 py-2 font-medium">Motivo fallo</th>
            </tr>
          </thead>
          <tbody>
            {loading && rows.length === 0 ? (
              <tr><td colSpan={8} className="px-3 py-8 text-center text-gray-400">Cargando…</td></tr>
            ) : rows.length === 0 ? (
              <tr><td colSpan={8} className="px-3 py-8 text-center text-gray-400">Sin registros.</td></tr>
            ) : rows.map(r => (
              <tr key={r.id} className={`border-t text-xs ${r.success ? 'hover:bg-gray-50' : 'bg-red-50 hover:bg-red-100'}`}>
                <td className="px-3 py-1.5 whitespace-nowrap"><Timestamp value={r.timestamp} /></td>
                <td className="px-3 py-1.5">
                  <span className={`px-1.5 py-0 rounded text-[10px] font-bold ${r.success ? 'bg-green-100 text-green-700' : 'bg-red-200 text-red-700'}`}>
                    {r.success ? 'OK' : 'FAIL'}
                  </span>
                </td>
                <td className="px-3 py-1.5"><LogonTypeBadge type={r.logonType} name={r.logonTypeName} /></td>
                <td className="px-3 py-1.5 font-medium text-gray-800">
                  {r.domain ? `${r.domain}\\${r.userName}` : (r.userName ?? '—')}
                </td>
                <td className="px-3 py-1.5 font-mono text-gray-600">
                  {r.sourceIp
                    ? <>{r.sourceIp}{r.sourcePort ? <span className="text-gray-400">:{r.sourcePort}</span> : null}</>
                    : '—'}
                </td>
                <td className="px-3 py-1.5 text-gray-500">{r.workstationName || '—'}</td>
                <td className="px-3 py-1.5 text-gray-400">{r.authPackage || '—'}</td>
                <td className="px-3 py-1.5 text-red-600">{r.failureReason || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Pagination page={page} pageSize={PAGE_SIZE} total={total} onPageChange={onPage} />
    </>
  );
}

// ─── Panel de conexiones TCP activas ────────────────────────────────────────

const SUSPICIOUS_PORTS = new Set([3389, 5985, 5986, 22, 23, 4444, 4445, 1080, 9001]);

function PortBadge({ port }: { port: number }) {
  const labels: Record<number, string> = {
    3389: 'RDP', 5985: 'WinRM', 5986: 'WinRM-S',
    22: 'SSH', 23: 'Telnet', 4444: 'Meterpreter',
    1080: 'SOCKS', 9001: 'Tor',
  };
  const label = labels[port] ?? String(port);
  const suspicious = SUSPICIOUS_PORTS.has(port);
  return (
    <span className={`text-xs font-mono px-1.5 py-0 rounded ${suspicious ? 'bg-orange-100 text-orange-700 font-bold' : 'text-gray-700'}`}>
      {label}
    </span>
  );
}

function ConnectionsSection() {
  const [snap, setSnap] = useState<ConnectionsSnapshot | null>(null);
  const [error, setError] = useState('');
  const [lastAt, setLastAt] = useState('');

  const load = useCallback(() => {
    getConnections()
      .then(s => { setSnap(s); setError(''); setLastAt(new Date().toLocaleTimeString()); })
      .catch(() => setError('No se pueden cargar las conexiones.'));
  }, []);

  useEffect(() => {
    load();
    const id = setInterval(load, 5_000);
    return () => clearInterval(id);
  }, [load]);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-gray-700">Conexiones TCP activas</h3>
        <span className="text-xs text-gray-400">Refresco 5 s · {lastAt}</span>
      </div>

      {error && <div className="text-red-600 text-sm">{error}</div>}

      {snap && (
        <>
          {/* Puertos en escucha */}
          <div className="bg-white rounded-xl border shadow-sm overflow-hidden">
            <div className="px-4 py-2 bg-gray-50 border-b text-xs font-semibold text-gray-600">
              Puertos en escucha ({snap.listening.length})
            </div>
            <div className="overflow-x-auto max-h-56 overflow-y-auto">
              <table className="w-full text-xs">
                <thead>
                  <tr className="text-left text-gray-400 border-b">
                    <th className="px-3 py-1.5 font-medium">Puerto</th>
                    <th className="px-3 py-1.5 font-medium">IP local</th>
                    <th className="px-3 py-1.5 font-medium">PID</th>
                    <th className="px-3 py-1.5 font-medium">Proceso</th>
                  </tr>
                </thead>
                <tbody>
                  {snap.listening.length === 0 ? (
                    <tr><td colSpan={4} className="px-3 py-4 text-center text-gray-400">Sin entradas</td></tr>
                  ) : snap.listening.map((r, i) => (
                    <tr key={i} className={`border-t ${SUSPICIOUS_PORTS.has(r.localPort) ? 'bg-orange-50' : 'hover:bg-gray-50'}`}>
                      <td className="px-3 py-1"><PortBadge port={r.localPort} /></td>
                      <td className="px-3 py-1 font-mono text-gray-500">{r.localIp}</td>
                      <td className="px-3 py-1 text-gray-400">{r.pid}</td>
                      <td className="px-3 py-1 font-medium text-gray-700">{r.processName}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {/* Conexiones establecidas */}
          <div className="bg-white rounded-xl border shadow-sm overflow-hidden">
            <div className="px-4 py-2 bg-gray-50 border-b text-xs font-semibold text-gray-600">
              Conexiones establecidas ({snap.established.length})
              {snap.established.some(r => SUSPICIOUS_PORTS.has(r.remotePort) || SUSPICIOUS_PORTS.has(r.localPort)) && (
                <span className="ml-2 text-orange-600">⚠ puerto sospechoso</span>
              )}
            </div>
            <div className="overflow-x-auto max-h-64 overflow-y-auto">
              <table className="w-full text-xs">
                <thead>
                  <tr className="text-left text-gray-400 border-b">
                    <th className="px-3 py-1.5 font-medium">Puerto local</th>
                    <th className="px-3 py-1.5 font-medium">IP remota</th>
                    <th className="px-3 py-1.5 font-medium">Puerto remoto</th>
                    <th className="px-3 py-1.5 font-medium">PID</th>
                    <th className="px-3 py-1.5 font-medium">Proceso</th>
                  </tr>
                </thead>
                <tbody>
                  {snap.established.length === 0 ? (
                    <tr><td colSpan={5} className="px-3 py-4 text-center text-gray-400">Sin conexiones activas</td></tr>
                  ) : snap.established.map((r, i) => {
                    const sus = SUSPICIOUS_PORTS.has(r.remotePort) || SUSPICIOUS_PORTS.has(r.localPort);
                    return (
                      <tr key={i} className={`border-t ${sus ? 'bg-orange-50 font-medium' : 'hover:bg-gray-50'}`}>
                        <td className="px-3 py-1"><PortBadge port={r.localPort} /></td>
                        <td className="px-3 py-1 font-mono text-gray-600">{r.remoteIp}</td>
                        <td className="px-3 py-1"><PortBadge port={r.remotePort} /></td>
                        <td className="px-3 py-1 text-gray-400">{r.pid}</td>
                        <td className="px-3 py-1 text-gray-700">{r.processName}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

export function AccessPanel() {
  const [summary, setSummary] = useState<LogonSummary | null>(null);
  const [rows, setRows] = useState<LogonEvent[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  // Filtros
  const [user, setUser]           = useState('');
  const [sourceIp, setSourceIp]   = useState('');
  const [remoteOnly, setRemoteOnly] = useState(false);
  const [onlyFails, setOnlyFails] = useState(false);
  const [from, setFrom]           = useState('');
  const [to, setTo]               = useState('');

  const loadSummary = useCallback(() => {
    getLogonSummary()
      .then(setSummary)
      .catch(() => {});
  }, []);

  const loadRows = useCallback((p: number) => {
    setLoading(true);
    getLogons({
      page: p,
      pageSize: PAGE_SIZE,
      user:       user || undefined,
      sourceIp:   sourceIp || undefined,
      remoteOnly: remoteOnly || undefined,
      success:    onlyFails ? false : undefined,
      from:       from || undefined,
      to:         to || undefined,
    })
      .then(r => { setRows(r.data); setTotal(r.total); setError(''); })
      .catch((e: { response?: { status?: number } }) => {
        if (e?.response?.status === 404) {
          setError('El servicio no tiene esta funcionalidad activa. Reinícialo después de la última compilación.');
        } else {
          setError('No se puede conectar con el servicio. ¿Está corriendo como administrador?');
        }
      })
      .finally(() => setLoading(false));
  }, [user, sourceIp, remoteOnly, onlyFails, from, to]);

  useEffect(() => {
    loadSummary();
    loadRows(1);
    setPage(1);
  }, [user, sourceIp, remoteOnly, onlyFails, from, to]);

  useEffect(() => { loadRows(page); }, [page]);

  useEffect(() => {
    loadSummary();
    const id = setInterval(loadSummary, 30_000);
    return () => clearInterval(id);
  }, []);

  const handlePage = (p: number) => { setPage(p); loadRows(p); };

  return (
    <div className="space-y-5">

      {/* ── KPIs ── */}
      {summary && (
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          <KpiCard label="Fallos 24h"        value={summary.failures24h}      color={summary.failures24h > 10 ? 'text-red-600' : 'text-gray-800'} />
          <KpiCard label="Sesiones RDP 24h"  value={summary.rdpSessions24h}   color={summary.rdpSessions24h > 0 ? 'text-orange-600' : 'text-gray-800'} />
          <KpiCard label="Network logons 24h" value={summary.networkLogons24h} />
          <KpiCard label="IPs únicas 24h"    value={summary.uniqueSourceIps}  />
        </div>
      )}

      {/* ── Top atacantes ── */}
      {summary && summary.topAttackers.length > 0 && (
        <div className="bg-white rounded-xl border shadow-sm overflow-hidden">
          <div className="px-4 py-2 border-b text-sm font-semibold text-gray-700 flex items-center gap-2">
            <span className="text-red-500">⚠</span> Top IPs con logons fallidos (24h)
          </div>
          <div className="flex flex-wrap gap-2 p-3">
            {summary.topAttackers.map(a => (
              <span
                key={a.ip}
                className="px-3 py-1 rounded-full text-xs font-mono bg-red-50 text-red-700 border border-red-200 cursor-pointer hover:bg-red-100"
                onClick={() => setSourceIp(a.ip)}
                title="Filtrar por esta IP"
              >
                {a.ip} <span className="font-bold">×{a.count}</span>
              </span>
            ))}
          </div>
        </div>
      )}

      {/* ── Conexiones remotas recientes ── */}
      {summary && summary.recentRemote.length > 0 && (
        <div className="bg-white rounded-xl border shadow-sm overflow-hidden">
          <div className="px-4 py-2 border-b text-sm font-semibold text-gray-700">
            Accesos remotos recientes (24h)
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-xs">
              <thead>
                <tr className="bg-gray-50 text-left text-gray-500 border-b">
                  <th className="px-3 py-1.5 font-medium">Timestamp</th>
                  <th className="px-3 py-1.5 font-medium">Res.</th>
                  <th className="px-3 py-1.5 font-medium">Tipo</th>
                  <th className="px-3 py-1.5 font-medium">Usuario</th>
                  <th className="px-3 py-1.5 font-medium">IP Origen</th>
                </tr>
              </thead>
              <tbody>
                {summary.recentRemote.map(r => (
                  <tr key={r.id} className={`border-t ${r.success ? 'hover:bg-gray-50' : 'bg-red-50'}`}>
                    <td className="px-3 py-1 whitespace-nowrap"><Timestamp value={r.timestamp} /></td>
                    <td className="px-3 py-1">
                      <span className={`px-1 rounded text-[10px] font-bold ${r.success ? 'bg-green-100 text-green-700' : 'bg-red-200 text-red-700'}`}>
                        {r.success ? 'OK' : 'FAIL'}
                      </span>
                    </td>
                    <td className="px-3 py-1"><LogonTypeBadge type={r.logonType} name={r.logonTypeName} /></td>
                    <td className="px-3 py-1 font-medium">{r.userName ?? '—'}</td>
                    <td className="px-3 py-1 font-mono text-gray-600">{r.sourceIp ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── Filtros + tabla completa ── */}
      <div className="flex flex-wrap gap-2 items-end">
        <input
          type="text" placeholder="Usuario…" value={user}
          onChange={e => setUser(e.target.value)}
          className="border rounded px-2 py-1 text-sm w-40"
        />
        <input
          type="text" placeholder="IP origen…" value={sourceIp}
          onChange={e => setSourceIp(e.target.value)}
          className="border rounded px-2 py-1 text-sm w-40"
        />
        <input type="datetime-local" value={from} onChange={e => setFrom(e.target.value)}
          className="border rounded px-2 py-1 text-sm" title="Desde" />
        <input type="datetime-local" value={to} onChange={e => setTo(e.target.value)}
          className="border rounded px-2 py-1 text-sm" title="Hasta" />
        <button
          onClick={() => setRemoteOnly(v => !v)}
          className={`px-3 py-1 rounded text-sm border font-medium ${remoteOnly ? 'bg-blue-500 text-white border-blue-500' : 'bg-white text-gray-600 border-gray-300 hover:bg-gray-50'}`}
        >
          Solo remotos
        </button>
        <button
          onClick={() => setOnlyFails(v => !v)}
          className={`px-3 py-1 rounded text-sm border font-medium ${onlyFails ? 'bg-red-500 text-white border-red-500' : 'bg-white text-gray-600 border-gray-300 hover:bg-gray-50'}`}
        >
          Solo fallos
        </button>
        {(user || sourceIp || remoteOnly || onlyFails || from || to) && (
          <button
            onClick={() => { setUser(''); setSourceIp(''); setRemoteOnly(false); setOnlyFails(false); setFrom(''); setTo(''); }}
            className="text-xs text-gray-400 hover:text-gray-600 px-2 py-1 border rounded"
          >
            ✕ Limpiar
          </button>
        )}
        <button
          onClick={() => exportCsv(rows, [
            { key: 'timestamp',      header: 'Timestamp' },
            { key: 'success',        header: 'OK' },
            { key: 'logonTypeName',  header: 'Tipo' },
            { key: 'userName',       header: 'Usuario' },
            { key: 'domain',         header: 'Dominio' },
            { key: 'sourceIp',       header: 'IP Origen' },
            { key: 'sourcePort',     header: 'Puerto' },
            { key: 'workstationName',header: 'Estacion' },
            { key: 'authPackage',    header: 'Auth' },
            { key: 'failureReason',  header: 'Motivo' },
          ], 'accesos')}
          className="ml-auto border border-green-600 text-green-700 px-3 py-1 rounded text-sm hover:bg-green-50"
        >
          ↓ CSV
        </button>
        <button onClick={() => loadRows(page)}
          className="text-xs text-gray-400 hover:text-gray-600 border rounded px-2 py-1">
          ↺ Actualizar
        </button>
      </div>

      {error && <div className="text-red-600 text-sm">{error}</div>}

      <LogonTable rows={rows} total={total} page={page} onPage={handlePage} loading={loading} />

      {/* ── Conexiones TCP activas ── */}
      <div className="border-t pt-5 mt-2">
        <ConnectionsSection />
      </div>
    </div>
  );
}
