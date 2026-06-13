import { useState, useEffect } from 'react';
import { getProcessTimeline } from '../api/client';
import type { ProcessTimeline } from '../api/types';
import { Timestamp } from './Timestamp';

interface Props {
  pid: number;
  processName: string;
  onClose: () => void;
  onNavigateToTree?: (pid: number) => void;
}

type TimelineEntry = {
  type: 'process' | 'network' | 'dns' | 'alert' | 'advanced';
  timestamp: string;
  icon: string;
  title: string;
  detail: string;
  severity?: string;
};

function buildEntries(data: ProcessTimeline): TimelineEntry[] {
  const entries: TimelineEntry[] = [];

  for (const e of data.processes) {
    entries.push({
      type: 'process',
      timestamp: e.timestamp,
      icon: e.eventType === 'Create' ? '🟢' : '🔴',
      title: `Proceso ${e.eventType === 'Create' ? 'creado' : 'terminado'} [${e.eventSource}]`,
      detail: [
        e.commandLine,
        e.userName && `Usuario: ${e.userName}`,
        e.sha256    && `SHA256: ${e.sha256.slice(0, 12)}…`,
      ].filter(Boolean).join(' | ') || e.processName,
    });
  }

  for (const e of data.network) {
    entries.push({
      type: 'network',
      timestamp: e.timestamp,
      icon: '🌐',
      title: `Conexión → ${e.destinationIp}:${e.destinationPort}`,
      detail: [e.protocol, e.userName && `Usuario: ${e.userName}`, e.executablePath]
        .filter(Boolean).join(' | '),
    });
  }

  for (const e of data.dns) {
    entries.push({
      type: 'dns',
      timestamp: e.timestamp,
      icon: '🔍',
      title: `DNS: ${e.queryName}`,
      detail: [
        e.queryResults && `→ ${e.queryResults}`,
        e.queryStatus  && `(${e.queryStatus})`,
      ].filter(Boolean).join(' '),
    });
  }

  for (const e of data.alerts) {
    entries.push({
      type: 'alert',
      timestamp: e.timestamp,
      icon: e.severity === 'High' ? '🔴' : e.severity === 'Medium' ? '🟠' : '🟡',
      title: `Alerta: ${e.rule}${e.mitreTechnique ? ` [${e.mitreTechnique}]` : ''}`,
      detail: e.description,
      severity: e.severity,
    });
  }

  const advLabels: Record<number, string> = {
    7:  '📦 DLL cargada (Sysmon 7)',
    8:  '💉 CreateRemoteThread (Sysmon 8)',
    10: '🔓 ProcessAccess (Sysmon 10)',
  };
  for (const e of data.advanced) {
    entries.push({
      type: 'advanced',
      timestamp: e.timestamp,
      icon: '⚠',
      title: advLabels[e.eventId] ?? `Sysmon ID ${e.eventId}`,
      detail: [
        e.imagePath        && `Imagen: ${e.imagePath}`,
        e.targetProcessName && `Destino: ${e.targetProcessName} (PID ${e.targetPid})`,
        e.grantedAccess    && `Access: ${e.grantedAccess}`,
      ].filter(Boolean).join(' | '),
    });
  }

  return entries.sort((a, b) => a.timestamp.localeCompare(b.timestamp));
}

function entryRowClass(e: TimelineEntry): string {
  if (e.type === 'alert') {
    if (e.severity === 'High')   return 'bg-red-50 border border-red-200';
    if (e.severity === 'Medium') return 'bg-orange-50 border border-orange-200';
    return 'bg-yellow-50 border border-yellow-200';
  }
  if (e.type === 'advanced') return 'bg-purple-50 border border-purple-100';
  return 'bg-gray-50 border border-gray-100';
}

export function ProcessTimelineModal({ pid, processName, onClose, onNavigateToTree }: Props) {
  const [data, setData]       = useState<ProcessTimeline | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError]     = useState<string | null>(null);

  useEffect(() => {
    getProcessTimeline(pid)
      .then(setData)
      .catch(e => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, [pid]);

  const entries = data ? buildEntries(data) : [];

  return (
    <div
      className="fixed inset-0 z-50 flex justify-end"
      onClick={onClose}
    >
      {/* Panel deslizante desde la derecha */}
      <div
        className="relative bg-white w-full max-w-2xl h-full shadow-2xl flex flex-col"
        onClick={e => e.stopPropagation()}
      >
        {/* ── Header ── */}
        <div className="flex items-center justify-between px-5 py-3 border-b bg-gray-900 text-white flex-shrink-0">
          <div>
            <span className="font-semibold">{processName}</span>
            <span className="ml-2 font-mono text-gray-400 text-sm">PID {pid}</span>
          </div>
          <div className="flex items-center gap-2">
            {onNavigateToTree && (
              <button
                onClick={() => { onNavigateToTree(pid); onClose(); }}
                className="text-xs px-3 py-1 rounded bg-blue-600 hover:bg-blue-700"
              >
                🌳 Ver en árbol
              </button>
            )}
            <button
              onClick={onClose}
              className="ml-2 text-gray-400 hover:text-white text-xl leading-none"
            >
              ✕
            </button>
          </div>
        </div>

        {/* ── Body ── */}
        <div className="flex-1 overflow-y-auto p-4 space-y-1.5">
          {loading && (
            <p className="text-sm text-gray-400 py-10 text-center">Cargando timeline…</p>
          )}
          {error && (
            <p className="text-sm text-red-600 py-4">{error}</p>
          )}
          {!loading && entries.length === 0 && (
            <p className="text-sm text-gray-400 py-10 text-center">
              Sin eventos en la base de datos para PID {pid}.
            </p>
          )}
          {!loading && entries.length > 0 && (
            <p className="text-xs text-gray-400 mb-2">{entries.length} eventos</p>
          )}

          {entries.map((entry, i) => (
            <div key={i} className={`flex gap-3 p-2 rounded text-xs ${entryRowClass(entry)}`}>
              <span className="text-sm w-5 text-center flex-shrink-0">{entry.icon}</span>
              <div className="flex-1 min-w-0">
                <div className="flex flex-wrap items-baseline gap-2">
                  <span className="font-medium text-gray-800">{entry.title}</span>
                  <span className="text-gray-400 text-[10px]">
                    <Timestamp value={entry.timestamp} />
                  </span>
                </div>
                {entry.detail && (
                  <div
                    className="text-[11px] text-gray-500 mt-0.5 break-words"
                    title={entry.detail}
                  >
                    {entry.detail}
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>

        {/* ── Footer: stats ── */}
        {data && (
          <div className="border-t px-5 py-2 flex flex-wrap gap-4 text-xs text-gray-500 bg-gray-50 flex-shrink-0">
            <span>🟢 {data.processes.length} proc.</span>
            <span>🌐 {data.network.length} red</span>
            <span>🔍 {data.dns.length} DNS</span>
            <span>🔴 {data.alerts.length} alertas</span>
            {data.advanced.length > 0 && (
              <span>⚠ {data.advanced.length} Sysmon avanzado</span>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
