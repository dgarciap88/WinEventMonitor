import { createContext, useContext, useState } from 'react';

// ──────────────────────────────────────────────────────────────────────────────
// Contexto global de rango de fechas.
// Los panels lo consumen como valor por defecto para sus filtros internos.
// ──────────────────────────────────────────────────────────────────────────────

export interface DateRange {
  from: string; // ISO date string ("YYYY-MM-DD") o ""
  to:   string;
}

interface DateRangeCtx {
  range: DateRange;
  setRange: (r: DateRange) => void;
  clear: () => void;
}

const empty: DateRange = { from: '', to: '' };

const Ctx = createContext<DateRangeCtx>({
  range:    empty,
  setRange: () => {},
  clear:    () => {},
});

export const useDateRange = () => useContext(Ctx);

export function DateRangeProvider({ children }: { children: React.ReactNode }) {
  const [range, setRange] = useState<DateRange>(empty);
  const clear = () => setRange(empty);

  return (
    <Ctx.Provider value={{ range, setRange, clear }}>
      {children}
    </Ctx.Provider>
  );
}

// ─── Widget de cabecera ───────────────────────────────────────────────────────

export function DateRangeWidget() {
  const { range, setRange, clear } = useDateRange();
  const hasRange = range.from || range.to;

  return (
    <div className="flex items-center gap-2 ml-auto">
      {hasRange && (
        <span className="text-xs bg-blue-900 text-blue-200 px-2 py-0.5 rounded-full">
          Filtro activo
        </span>
      )}
      <label className="text-xs text-gray-400">Desde</label>
      <input
        type="date"
        value={range.from}
        max={range.to || undefined}
        onChange={e => setRange({ ...range, from: e.target.value })}
        className="text-xs bg-gray-800 text-gray-200 border border-gray-700 rounded px-2 py-0.5 focus:outline-none focus:ring-1 focus:ring-blue-500"
      />
      <label className="text-xs text-gray-400">Hasta</label>
      <input
        type="date"
        value={range.to}
        min={range.from || undefined}
        onChange={e => setRange({ ...range, to: e.target.value })}
        className="text-xs bg-gray-800 text-gray-200 border border-gray-700 rounded px-2 py-0.5 focus:outline-none focus:ring-1 focus:ring-blue-500"
      />
      {hasRange && (
        <button
          onClick={clear}
          className="text-xs text-gray-400 hover:text-gray-200 px-1"
          title="Limpiar filtro de fecha"
        >
          ✕
        </button>
      )}
    </div>
  );
}
