import { useState, useEffect, useCallback } from 'react';
import { getUnifiedTimeline } from '../api/client';
import type { TimelineItem } from '../api/types';
import { Timestamp } from './Timestamp';

const KIND_META: Record<TimelineItem['kind'], { label: string; color: string; lane: number }> = {
  alert:   { label: 'Alertas',  color: '#ef4444', lane: 0 },
  logon:   { label: 'Accesos',  color: '#8b5cf6', lane: 1 },
  network: { label: 'Red',      color: '#10b981', lane: 2 },
  dns:     { label: 'DNS',      color: '#f59e0b', lane: 3 },
  process: { label: 'Procesos', color: '#3b82f6', lane: 4 },
};
const LANES = Object.values(KIND_META).sort((a, b) => a.lane - b.lane);

const RANGES = [
  { id: '1h',  label: '1 h' },
  { id: '6h',  label: '6 h' },
  { id: '24h', label: '24 h' },
] as const;
type Range = typeof RANGES[number]['id'];

function SwimlaneChart({ items, onSelect }: { items: TimelineItem[]; onSelect: (item: TimelineItem) => void }) {
  if (items.length === 0) {
    return <p className="text-xs text-gray-400 py-8 text-center">Sin actividad en esta ventana de tiempo.</p>;
  }

  const W = 900, laneH = 30, pad = 6;
  const H = LANES.length * laneH + pad * 2;
  const times = items.map(i => new Date(i.timestamp).getTime());
  const minT = Math.min(...times), maxT = Math.max(...times);
  const x = (t: number) => pad + ((t - minT) / (maxT - minT || 1)) * (W - pad * 2);
  const y = (lane: number) => pad + lane * laneH + laneH / 2;

  return (
    <div className="flex gap-2">
      <div className="flex flex-col justify-around text-[10px] text-gray-500 pt-1.5 pb-1.5" style={{ height: H }}>
        {LANES.map(l => (
          <span key={l.label} style={{ height: laneH }} className="flex items-center gap-1">
            <span className="inline-block w-2 h-2 rounded-full" style={{ background: l.color }} />
            {l.label}
          </span>
        ))}
      </div>
      <svg viewBox={`0 0 ${W} ${H}`} className="flex-1 overflow-visible" preserveAspectRatio="none">
        {LANES.map((l, i) => (
          <line key={i} x1={0} x2={W} y1={y(l.lane)} y2={y(l.lane)} stroke="#f3f4f6" strokeWidth={laneH} />
        ))}
        {items.map((item, i) => {
          const meta = KIND_META[item.kind];
          const isHighSeverity = item.severity === 'High';
          return (
            <circle
              key={i}
              cx={x(new Date(item.timestamp).getTime())}
              cy={y(meta.lane)}
              r={isHighSeverity ? 5 : 3}
              fill={meta.color}
              opacity={isHighSeverity ? 1 : 0.7}
              className="cursor-pointer hover:opacity-100"
              onClick={() => onSelect(item)}
            >
              <title>{`${item.summary} — ${new Date(item.timestamp).toLocaleString()}`}</title>
            </circle>
          );
        })}
      </svg>
    </div>
  );
}

export function TimelinePanel() {
  const [range, setRange] = useState<Range>('6h');
  const [items, setItems] = useState<TimelineItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<TimelineItem | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    getUnifiedTimeline(range)
      .then(setItems)
      .catch(() => setError('Error conectando con el servicio.'))
      .finally(() => setLoading(false));
  }, [range]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    const id = setInterval(load, 60_000);
    return () => clearInterval(id);
  }, [load]);

  const sorted = [...items].sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="text-base font-semibold text-gray-700">Cronología</h2>
        <div className="flex items-center gap-2">
          <div className="flex gap-1">
            {RANGES.map(r => (
              <button
                key={r.id}
                onClick={() => setRange(r.id)}
                className={`text-xs px-2 py-1 rounded border ${
                  range === r.id ? 'bg-blue-600 text-white border-blue-600' : 'text-gray-500 border-gray-200 hover:bg-gray-50'
                }`}
              >
                {r.label}
              </button>
            ))}
          </div>
          <button onClick={load} className="text-xs text-gray-400 hover:text-gray-600 border rounded px-2 py-1">
            ↺ Actualizar
          </button>
        </div>
      </div>

      {error && (
        <div className="text-sm text-red-600 bg-red-50 border border-red-200 rounded px-3 py-2">{error}</div>
      )}

      <div className="bg-white rounded-xl border shadow-sm p-4">
        {loading && items.length === 0 ? (
          <p className="text-xs text-gray-400 py-8 text-center">Cargando…</p>
        ) : (
          <>
            <SwimlaneChart items={items} onSelect={setSelected} />
            {items.length > 0 && (
              <p className="text-[10px] text-gray-400 mt-2">
                {items.length} eventos · haz clic en un punto para ver el detalle
              </p>
            )}
          </>
        )}
      </div>

      {selected && (
        <div className="bg-blue-50 border border-blue-200 rounded-lg px-4 py-3 flex items-start justify-between gap-3">
          <div>
            <div className="flex items-center gap-2 text-xs text-blue-700 font-semibold">
              <span className="inline-block w-2 h-2 rounded-full" style={{ background: KIND_META[selected.kind].color }} />
              {KIND_META[selected.kind].label}
              {selected.severity && <span className="px-1.5 py-0.5 rounded bg-red-100 text-red-700">{selected.severity}</span>}
            </div>
            <p className="text-sm text-gray-800 mt-1">{selected.summary}</p>
            <p className="text-xs text-gray-500 mt-0.5">
              <Timestamp value={selected.timestamp} />
              {selected.processName && <> · {selected.processName}</>}
              {selected.pid != null && <> · PID {selected.pid}</>}
            </p>
          </div>
          <button onClick={() => setSelected(null)} className="text-gray-400 hover:text-gray-600 text-lg leading-none">×</button>
        </div>
      )}

      {/* Lista de eventos recientes, sincronizada con el rango elegido */}
      {sorted.length > 0 && (
        <div className="bg-white rounded-xl border shadow-sm overflow-hidden">
          <div className="px-4 py-2 border-b text-xs font-semibold text-gray-600">Eventos ({sorted.length})</div>
          <div className="max-h-96 overflow-y-auto divide-y divide-gray-50">
            {sorted.map((item, i) => {
              const meta = KIND_META[item.kind];
              return (
                <div
                  key={i}
                  onClick={() => setSelected(item)}
                  className="px-4 py-1.5 text-xs flex items-center gap-2 hover:bg-gray-50 cursor-pointer"
                >
                  <span className="inline-block w-2 h-2 rounded-full flex-shrink-0" style={{ background: meta.color }} />
                  <span className="text-gray-400 w-16 flex-shrink-0"><Timestamp value={item.timestamp} /></span>
                  {item.severity && (
                    <span className="px-1 py-0.5 rounded bg-red-100 text-red-700 text-[10px] flex-shrink-0">{item.severity}</span>
                  )}
                  <span className="text-gray-700 truncate">{item.summary}</span>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
