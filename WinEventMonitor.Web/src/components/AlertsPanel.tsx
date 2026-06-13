import { useState, useEffect, useCallback } from 'react';
import { getAlerts } from '../api/client';
import type { AlertEvent } from '../api/types';
import { exportCsv } from '../utils/exportCsv';
import { Timestamp } from './Timestamp';
import { Pagination } from './Pagination';

function SeverityBadge({ severity }: { severity: string }) {
  const map: Record<string, string> = {
    High:   'bg-red-100 text-red-700 border border-red-300',
    Medium: 'bg-orange-100 text-orange-700 border border-orange-300',
    Low:    'bg-yellow-100 text-yellow-700 border border-yellow-300',
  };
  return (
    <span className={`px-2 py-0.5 rounded text-xs font-semibold ${map[severity] ?? 'bg-gray-100 text-gray-600'}`}>
      {severity}
    </span>
  );
}

export function AlertsPanel({ onNavigateToTree }: { onNavigateToTree?: (pid: number) => void }) {
  const [page, setPage]       = useState(1);
  const [rows, setRows]       = useState<AlertEvent[]>([]);
  const [total, setTotal]     = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError]     = useState<string | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);

  const PAGE_SIZE = 50;

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const r = await getAlerts(page, PAGE_SIZE);
      setRows(r.data);
      setTotal(r.total);
    } catch {
      setError('Error conectando con el servicio.');
    } finally {
      setLoading(false);
    }
  }, [page]);

  useEffect(() => { load(); }, [load]);

  // Auto-refresco cada 30 s
  useEffect(() => {
    const id = setInterval(load, 30_000);
    return () => clearInterval(id);
  }, [load]);

  const highCount   = rows.filter(r => r.severity === 'High').length;
  const mediumCount = rows.filter(r => r.severity === 'Medium').length;

  return (
    <div className="space-y-3">
      {/* ── Resumen ── */}
      <div className="flex flex-wrap gap-3 items-center">
        <div className="flex gap-2 text-sm">
          {highCount > 0 && (
            <span className="px-3 py-1 rounded-full bg-red-100 text-red-700 font-semibold">
              🔴 {highCount} alta{highCount !== 1 ? 's' : ''} (en página actual)
            </span>
          )}
          {mediumCount > 0 && (
            <span className="px-3 py-1 rounded-full bg-orange-100 text-orange-700 font-semibold">
              🟠 {mediumCount} media{mediumCount !== 1 ? 's' : ''}
            </span>
          )}
          {total === 0 && !loading && (
            <span className="text-gray-400 text-sm">Sin alertas — el sistema está limpio</span>
          )}
        </div>
        <button
          onClick={load}
          className="ml-auto text-xs text-gray-400 hover:text-gray-600 border rounded px-2 py-1"
        >
          ↺ Actualizar
        </button>
        <button
          className="border border-green-600 text-green-700 px-3 py-1 rounded text-xs hover:bg-green-50"
          onClick={() => exportCsv(
            rows,
            [
              { key: 'timestamp',   header: 'Timestamp' },
              { key: 'severity',    header: 'Severidad' },
              { key: 'rule',        header: 'Regla' },
              { key: 'description', header: 'Descripcion' },
              { key: 'pid',         header: 'PID' },
              { key: 'processName', header: 'Proceso' },
              { key: 'details',     header: 'Detalles' },
            ],
            'alertas'
          )}
          title="Exportar pagina actual como CSV"
        >
          ↓ CSV
        </button>
      </div>

      {error && (
        <div className="text-sm text-red-600 bg-red-50 border border-red-200 rounded px-3 py-2">
          {error}
        </div>
      )}

      {loading && rows.length === 0 && (
        <p className="text-sm text-gray-400 py-8 text-center">Cargando alertas…</p>
      )}

      {/* ── Tabla de alertas ── */}
      {rows.length > 0 && (
        <div className="rounded-lg border border-gray-200 bg-white overflow-auto">
          <table className="w-full text-xs">
            <thead className="bg-gray-50 border-b sticky top-0 z-10">
              <tr>
                <th className="px-3 py-2 text-left font-semibold text-gray-600 w-36">Timestamp</th>
                <th className="px-3 py-2 text-left font-semibold text-gray-600 w-20">Severidad</th>
                <th className="px-3 py-2 text-left font-semibold text-gray-600 w-48">Regla</th>
                <th className="px-3 py-2 text-left font-semibold text-gray-600">Descripción</th>
                <th className="px-3 py-2 text-left font-semibold text-gray-600 w-16">PID</th>
                <th className="px-3 py-2 text-left font-semibold text-gray-600 w-36">Proceso</th>
                <th className="px-3 py-2 w-6"></th>
                <th className="px-3 py-2 w-8"></th>
              </tr>
            </thead>
            <tbody>
              {rows.map(row => (
                <>
                  <tr
                    key={row.id}
                    className={`border-b border-gray-100 cursor-pointer ${
                      row.severity === 'High'
                        ? 'bg-red-50 hover:bg-red-100'
                        : row.severity === 'Medium'
                          ? 'bg-orange-50 hover:bg-orange-100'
                          : 'hover:bg-gray-50'
                    }`}
                    onClick={() => setExpanded(prev => prev === row.id ? null : row.id)}
                  >
                    <td className="px-3 py-1.5 text-gray-500">
                      <Timestamp value={row.timestamp} />
                    </td>
                    <td className="px-3 py-1.5">
                      <SeverityBadge severity={row.severity} />
                    </td>
                    <td className="px-3 py-1.5 font-medium text-gray-700">
                      <span>{row.rule}</span>
                      {row.mitreTechnique && (
                        <a
                          href={`https://attack.mitre.org/techniques/${row.mitreTechnique.replace('.', '/')}/`}
                          target="_blank"
                          rel="noopener noreferrer"
                          onClick={e => e.stopPropagation()}
                          className="ml-1.5 text-[10px] font-mono bg-blue-100 text-blue-700 px-1 py-0.5 rounded hover:bg-blue-200"
                          title="Ver en MITRE ATT&CK"
                        >
                          {row.mitreTechnique}
                        </a>
                      )}
                    </td>
                    <td className="px-3 py-1.5 text-gray-600">{row.description}</td>
                    <td className="px-3 py-1.5 font-mono text-gray-400">{row.pid ?? '—'}</td>
                    <td className="px-3 py-1.5 text-gray-600">{row.processName ?? '—'}</td>
                    <td className="px-3 py-1.5 text-gray-400">
                      {expanded === row.id ? '▲' : '▼'}
                    </td>
                    <td className="px-3 py-1.5">
                      {row.pid != null && onNavigateToTree && (
                        <button
                          title="Ver en árbol de procesos"
                          onClick={e => { e.stopPropagation(); onNavigateToTree(row.pid!); }}
                          className="text-blue-500 hover:text-blue-700 text-xs px-1"
                        >
                          🌲
                        </button>
                      )}
                    </td>
                  </tr>
                  {expanded === row.id && (
                    <tr key={`${row.id}-details`} className="bg-gray-50 border-b border-gray-200">
                      <td colSpan={8} className="px-4 py-2">
                        <div className="text-xs font-mono text-gray-600 whitespace-pre-wrap break-all">
                          {row.details ?? '(sin detalles adicionales)'}
                        </div>
                      </td>
                    </tr>
                  )}
                </>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Pagination
        page={page}
        pageSize={PAGE_SIZE}
        total={total}
        onPageChange={setPage}
      />

      <p className="text-xs text-gray-400">
        Las alertas se generan cada 60 s. Auto-refresco cada 30 s.
      </p>
    </div>
  );
}
