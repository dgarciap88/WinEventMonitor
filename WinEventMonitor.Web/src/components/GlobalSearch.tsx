import { useState, useEffect, useCallback, useRef } from 'react';
import { getProcesses, getNetwork, getDns, getAlerts } from '../api/client';
import type { ProcessEvent, NetworkEvent, DnsEvent, AlertEvent } from '../api/types';
import { Timestamp } from './Timestamp';

interface Props {
  onClose: () => void;
  onNavigateToTab: (tab: string) => void;
  onNavigateToTree: (pid: number) => void;
}

interface SearchResults {
  processes: ProcessEvent[];
  network: NetworkEvent[];
  dns: DnsEvent[];
  alerts: AlertEvent[];
}

export function GlobalSearch({ onClose, onNavigateToTab, onNavigateToTree }: Props) {
  const [query, setQuery]           = useState('');
  const [results, setResults]       = useState<SearchResults | null>(null);
  const [loading, setLoading]       = useState(false);
  const inputRef                    = useRef<HTMLInputElement>(null);
  const debounceRef                 = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  // Cerrar con ESC
  useEffect(() => {
    const handler = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onClose]);

  const search = useCallback(async (q: string) => {
    if (!q.trim() || q.length < 2) { setResults(null); return; }
    setLoading(true);
    try {
      const isPid = /^\d+$/.test(q.trim());

      const [procs, net, dns, alertsPage] = await Promise.all([
        getProcesses({ name: isPid ? undefined : q, page: 1, pageSize: 5 }).catch(() => ({ data: [] as ProcessEvent[] })),
        getNetwork({ process: isPid ? undefined : q, destIp: isPid ? undefined : q, page: 1, pageSize: 5 }).catch(() => ({ data: [] as NetworkEvent[] })),
        getDns({ domain: isPid ? undefined : q, process: isPid ? undefined : q, page: 1, pageSize: 5 }).catch(() => ({ data: [] as DnsEvent[] })),
        getAlerts(1, 20).catch(() => ({ data: [] as AlertEvent[] })),
      ]);

      const ql = q.toLowerCase();
      const filteredAlerts = isPid
        ? (alertsPage.data ?? []).filter(a => a.pid === parseInt(q))
        : (alertsPage.data ?? []).filter(a =>
            a.rule.toLowerCase().includes(ql) ||
            a.description.toLowerCase().includes(ql) ||
            (a.processName?.toLowerCase().includes(ql) ?? false)
          ).slice(0, 5);

      setResults({
        processes: procs.data ?? [],
        network:   net.data   ?? [],
        dns:       dns.data   ?? [],
        alerts:    filteredAlerts,
      });
    } finally {
      setLoading(false);
    }
  }, []);

  const handleInput = (v: string) => {
    setQuery(v);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => search(v), 300);
  };

  const total = results
    ? results.processes.length + results.network.length + results.dns.length + results.alerts.length
    : 0;

  return (
    <div
      className="fixed inset-0 z-50 bg-black/40 flex items-start justify-center pt-[8vh]"
      onClick={onClose}
    >
      <div
        className="bg-white rounded-xl shadow-2xl w-full max-w-xl overflow-hidden"
        onClick={e => e.stopPropagation()}
      >
        {/* ── Input ── */}
        <div className="flex items-center gap-3 px-4 py-3 border-b">
          <span className="text-gray-400">🔍</span>
          <input
            ref={inputRef}
            value={query}
            onChange={e => handleInput(e.target.value)}
            placeholder="Buscar proceso, IP, dominio, PID, alerta…"
            className="flex-1 outline-none text-sm text-gray-800"
          />
          <kbd className="text-[10px] bg-gray-100 text-gray-500 px-1.5 py-0.5 rounded border border-gray-200">
            ESC
          </kbd>
        </div>

        {/* ── Resultados ── */}
        <div className="max-h-[60vh] overflow-y-auto">
          {loading && (
            <div className="py-6 text-center text-sm text-gray-400">Buscando…</div>
          )}

          {!loading && query.length >= 2 && results && total === 0 && (
            <div className="py-6 text-center text-sm text-gray-400">
              Sin resultados para "{query}"
            </div>
          )}

          {!loading && results && total > 0 && (
            <div className="divide-y divide-gray-100">

              {/* Procesos */}
              {results.processes.length > 0 && (
                <section>
                  <div className="px-4 py-1.5 text-[10px] font-semibold text-gray-400 uppercase tracking-wider bg-gray-50">
                    Procesos ({results.processes.length})
                  </div>
                  {results.processes.map(p => (
                    <button
                      key={p.id}
                      className="w-full flex items-center gap-3 px-4 py-2 hover:bg-blue-50 text-left"
                      onClick={() => { onNavigateToTree(p.pid); onClose(); }}
                    >
                      <span>💻</span>
                      <div className="flex-1 min-w-0">
                        <div className="text-xs font-medium text-gray-800 truncate">
                          {p.processName}
                          <span className="ml-2 font-mono text-gray-400">PID {p.pid}</span>
                        </div>
                        <div className="text-[11px] text-gray-400">
                          <Timestamp value={p.timestamp} />
                        </div>
                      </div>
                      <span className="text-[10px] text-blue-500 flex-shrink-0">→ Árbol</span>
                    </button>
                  ))}
                </section>
              )}

              {/* Red */}
              {results.network.length > 0 && (
                <section>
                  <div className="px-4 py-1.5 text-[10px] font-semibold text-gray-400 uppercase tracking-wider bg-gray-50">
                    Red ({results.network.length})
                  </div>
                  {results.network.map(n => (
                    <button
                      key={n.id}
                      className="w-full flex items-center gap-3 px-4 py-2 hover:bg-blue-50 text-left"
                      onClick={() => { onNavigateToTab('network'); onClose(); }}
                    >
                      <span>🌐</span>
                      <div className="flex-1 min-w-0">
                        <div className="text-xs font-medium text-gray-800 truncate">
                          {n.processName} → {n.destinationIp}:{n.destinationPort}
                        </div>
                        <div className="text-[11px] text-gray-400">
                          <Timestamp value={n.timestamp} />
                        </div>
                      </div>
                      <span className="text-[10px] text-blue-500 flex-shrink-0">→ Red</span>
                    </button>
                  ))}
                </section>
              )}

              {/* DNS */}
              {results.dns.length > 0 && (
                <section>
                  <div className="px-4 py-1.5 text-[10px] font-semibold text-gray-400 uppercase tracking-wider bg-gray-50">
                    DNS ({results.dns.length})
                  </div>
                  {results.dns.map(d => (
                    <button
                      key={d.id}
                      className="w-full flex items-center gap-3 px-4 py-2 hover:bg-blue-50 text-left"
                      onClick={() => { onNavigateToTab('dns'); onClose(); }}
                    >
                      <span>🔍</span>
                      <div className="flex-1 min-w-0">
                        <div className="text-xs font-medium text-gray-800 truncate">{d.queryName}</div>
                        <div className="text-[11px] text-gray-400">
                          {d.processName} · <Timestamp value={d.timestamp} />
                        </div>
                      </div>
                      <span className="text-[10px] text-blue-500 flex-shrink-0">→ DNS</span>
                    </button>
                  ))}
                </section>
              )}

              {/* Alertas */}
              {results.alerts.length > 0 && (
                <section>
                  <div className="px-4 py-1.5 text-[10px] font-semibold text-gray-400 uppercase tracking-wider bg-gray-50">
                    Alertas ({results.alerts.length})
                  </div>
                  {results.alerts.map(a => (
                    <button
                      key={a.id}
                      className="w-full flex items-center gap-3 px-4 py-2 hover:bg-red-50 text-left"
                      onClick={() => { onNavigateToTab('alerts'); onClose(); }}
                    >
                      <span>{a.severity === 'High' ? '🔴' : a.severity === 'Medium' ? '🟠' : '🟡'}</span>
                      <div className="flex-1 min-w-0">
                        <div className="text-xs font-medium text-gray-800 truncate">{a.rule}</div>
                        <div className="text-[11px] text-gray-400 truncate">
                          {a.processName ?? ''} · <Timestamp value={a.timestamp} />
                        </div>
                      </div>
                      <span className="text-[10px] text-blue-500 flex-shrink-0">→ Alertas</span>
                    </button>
                  ))}
                </section>
              )}
            </div>
          )}

          {!query && (
            <div className="px-4 py-8 text-sm text-gray-400 text-center">
              Escribe para buscar en procesos, red, DNS y alertas
              <br />
              <span className="text-xs">Acepta nombres, IPs, dominios o PID numérico</span>
            </div>
          )}
        </div>

        {/* ── Footer ── */}
        <div className="border-t px-4 py-2 flex items-center justify-between text-[11px] text-gray-400 bg-gray-50">
          <span>
            <kbd className="bg-gray-100 px-1 py-0.5 rounded border border-gray-200">Ctrl+K</kbd> abre ·{' '}
            <kbd className="bg-gray-100 px-1 py-0.5 rounded border border-gray-200">ESC</kbd> cierra
          </span>
          {total > 0 && <span>{total} resultados</span>}
        </div>
      </div>
    </div>
  );
}
