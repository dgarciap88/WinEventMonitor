import { useState, useEffect, useCallback } from 'react';
import { getAlerts, updateAlertStatus } from '../api/client';
import type { AlertEvent } from '../api/types';
import { exportCsv } from '../utils/exportCsv';
import { getAlertExplanation } from '../utils/alertExplanations';
import { Timestamp } from './Timestamp';
import { Pagination } from './Pagination';

const SEVERITY_STYLE: Record<string, { badge: string; border: string; icon: string }> = {
  High:   { badge: 'bg-red-100 text-red-700 border border-red-300',       border: 'border-l-4 border-l-red-500',    icon: '🔴' },
  Medium: { badge: 'bg-orange-100 text-orange-700 border border-orange-300', border: 'border-l-4 border-l-orange-400', icon: '🟠' },
  Low:    { badge: 'bg-yellow-100 text-yellow-700 border border-yellow-300', border: 'border-l-4 border-l-yellow-400', icon: '🟡' },
};

const STATUS_LABEL: Record<string, string> = {
  Reviewed:  'Revisada',
  Dismissed: 'Descartada',
  Trusted:   'Confiable',
};

function SeverityBadge({ severity }: { severity: string }) {
  const style = SEVERITY_STYLE[severity] ?? { badge: 'bg-gray-100 text-gray-600 border border-gray-200', border: '', icon: '⚪' };
  return (
    <span className={`px-2 py-0.5 rounded text-xs font-semibold whitespace-nowrap ${style.badge}`}>
      {style.icon} {severity}
    </span>
  );
}

export function AlertsPanel({ onNavigateToTree }: { onNavigateToTree?: (pid: number) => void }) {
  const [page, setPage]         = useState(1);
  const [rows, setRows]         = useState<AlertEvent[]>([]);
  const [total, setTotal]       = useState(0);
  const [loading, setLoading]   = useState(false);
  const [error, setError]       = useState<string | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [onlyPending, setOnlyPending] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [actionMsg, setActionMsg] = useState<Record<string, string>>({});

  const PAGE_SIZE = 30;

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const r = await getAlerts(page, PAGE_SIZE, onlyPending ? 'New' : undefined);
      setRows(r.data);
      setTotal(r.total);
    } catch {
      setError('Error conectando con el servicio.');
    } finally {
      setLoading(false);
    }
  }, [page, onlyPending]);

  useEffect(() => { load(); }, [load]);

  // Auto-refresco cada 30 s
  useEffect(() => {
    const id = setInterval(load, 30_000);
    return () => clearInterval(id);
  }, [load]);

  // Volver a la primera página al cambiar el filtro
  useEffect(() => { setPage(1); }, [onlyPending]);

  const highCount   = rows.filter(r => r.severity === 'High').length;
  const mediumCount = rows.filter(r => r.severity === 'Medium').length;

  function errorDetail(err: unknown): string {
    const detail = (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail;
    return detail ?? 'Error inesperado.';
  }

  async function handleStatusChange(row: AlertEvent, status: 'Reviewed' | 'Dismissed' | 'Trusted') {
    setBusyId(row.id);
    try {
      const updated = await updateAlertStatus(row.id, status);
      setRows(rs => rs.map(r => (r.id === row.id ? updated : r)));
      setActionMsg(m => ({ ...m, [row.id]: status === 'Trusted'
        ? 'Marcada como confiable. No se volverá a avisar para este proceso.'
        : status === 'Dismissed' ? 'Alerta descartada.' : 'Alerta marcada como revisada.' }));
    } catch (err) {
      setActionMsg(m => ({ ...m, [row.id]: `Error: ${errorDetail(err)}` }));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="space-y-3">
      {/* ── Resumen y controles ── */}
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

        <label className="flex items-center gap-1.5 text-xs text-gray-500 select-none cursor-pointer">
          <input
            type="checkbox"
            checked={onlyPending}
            onChange={e => setOnlyPending(e.target.checked)}
          />
          Solo pendientes de revisar
        </label>

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
              { key: 'status',      header: 'Estado' },
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

      {!loading && rows.length === 0 && total === 0 && onlyPending && (
        <p className="text-sm text-gray-400 py-8 text-center">No hay alertas pendientes de revisar. 🎉</p>
      )}

      {/* ── Tarjetas de alertas ── */}
      {rows.length > 0 && (
        <div className="space-y-2">
          {rows.map(row => {
            const explanation = getAlertExplanation(row.rule);
            const style = SEVERITY_STYLE[row.severity] ?? { badge: '', border: 'border-l-4 border-l-gray-300', icon: '⚪' };
            const isExpanded = expanded === row.id;
            const isDismissed = row.status === 'Dismissed';

            return (
              <div
                key={row.id}
                className={`rounded-lg border border-gray-200 bg-white ${style.border} ${isDismissed ? 'opacity-50' : ''}`}
              >
                <div
                  className="p-3 cursor-pointer"
                  onClick={() => setExpanded(isExpanded ? null : row.id)}
                >
                  <div className="flex items-start justify-between gap-3 flex-wrap">
                    <div className="flex items-center gap-2 flex-wrap">
                      <SeverityBadge severity={row.severity} />
                      <span className="font-medium text-gray-800 text-sm">{row.rule}</span>
                      {row.status && row.status !== 'New' && (
                        <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-gray-100 text-gray-500">
                          {STATUS_LABEL[row.status] ?? row.status}
                        </span>
                      )}
                    </div>
                    <span className="text-xs text-gray-400 shrink-0 flex items-center gap-1.5">
                      <Timestamp value={row.timestamp} />
                      <span className="text-gray-300">{isExpanded ? '▲' : '▼'}</span>
                    </span>
                  </div>

                  <p className="text-sm text-gray-800 mt-1.5 leading-snug">
                    {explanation?.summary ?? row.description}
                  </p>
                  {explanation && (
                    <p className="text-xs text-gray-400 mt-0.5">{row.description}</p>
                  )}
                </div>

                {isExpanded && (
                  <div className="px-3 pb-3 border-t border-gray-100 pt-2 space-y-2">
                    {explanation && (
                      <p className="text-xs bg-blue-50 text-blue-800 rounded px-2 py-1.5">
                        <strong>Qué hacer:</strong> {explanation.action}
                      </p>
                    )}

                    <div className="flex items-center gap-3 flex-wrap text-xs text-gray-500">
                      {row.processName && (
                        <span>Proceso: <span className="font-mono text-gray-700">{row.processName}</span></span>
                      )}
                      {row.pid != null && (
                        <span>PID: <span className="font-mono text-gray-700">{row.pid}</span></span>
                      )}
                      {row.mitreTechnique && (
                        <a
                          href={`https://attack.mitre.org/techniques/${row.mitreTechnique.replace('.', '/')}/`}
                          target="_blank"
                          rel="noopener noreferrer"
                          onClick={e => e.stopPropagation()}
                          className="font-mono bg-blue-100 text-blue-700 px-1 py-0.5 rounded hover:bg-blue-200"
                          title="Ver en MITRE ATT&CK"
                        >
                          MITRE {row.mitreTechnique}
                        </a>
                      )}
                      {row.pid != null && onNavigateToTree && (
                        <button
                          onClick={e => { e.stopPropagation(); onNavigateToTree(row.pid!); }}
                          className="text-blue-500 hover:text-blue-700"
                        >
                          🌲 Ver en árbol de procesos
                        </button>
                      )}
                    </div>

                    {row.details && (
                      <div className="text-xs font-mono text-gray-600 bg-gray-50 rounded p-2 whitespace-pre-wrap break-all">
                        {row.details}
                      </div>
                    )}

                    <div className="flex items-center gap-2 flex-wrap pt-1 border-t border-gray-100">
                      <button
                        disabled={busyId === row.id}
                        onClick={e => { e.stopPropagation(); handleStatusChange(row, 'Dismissed'); }}
                        className="text-xs border border-gray-300 text-gray-600 px-2 py-1 rounded hover:bg-gray-50 disabled:opacity-50"
                      >
                        Descartar
                      </button>
                      <button
                        disabled={busyId === row.id}
                        onClick={e => { e.stopPropagation(); handleStatusChange(row, 'Trusted'); }}
                        className="text-xs border border-green-300 text-green-700 px-2 py-1 rounded hover:bg-green-50 disabled:opacity-50"
                        title="No volver a avisar de esta regla para este proceso"
                      >
                        ✓ Confío en esto
                      </button>
                      {actionMsg[row.id] && (
                        <span className="text-xs text-gray-500 italic">{actionMsg[row.id]}</span>
                      )}
                    </div>
                  </div>
                )}
              </div>
            );
          })}
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
