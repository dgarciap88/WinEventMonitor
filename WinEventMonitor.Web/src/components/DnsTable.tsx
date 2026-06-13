import { useState, useEffect } from 'react';
import { getDns } from '../api/client';
import type { DnsEvent, DnsFilters } from '../api/types';
import { exportCsv } from '../utils/exportCsv';
import { Timestamp } from './Timestamp';
import { Pagination } from './Pagination';
import { DateRangeFilter } from './DateRangeFilter';
import { useDateRange } from '../context/DateRangeContext';

export function DnsTable() {
  const { range } = useDateRange();
  const [filters, setFilters] = useState<DnsFilters>({ page: 1, pageSize: 50 });
  const [rows, setRows] = useState<DnsEvent[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [processInput, setProcessInput] = useState('');
  const [domainInput, setDomainInput] = useState('');
  const [excludeTrusted, setExcludeTrusted] = useState(false);
  const [fromInput, setFromInput] = useState(range.from);
  const [toInput, setToInput] = useState(range.to);

  // Sincronizar con el rango global al cambiar
  useEffect(() => {
    setFromInput(range.from);
    setToInput(range.to);
    setFilters(f => ({ ...f, page: 1, from: range.from || undefined, to: range.to || undefined }));
  }, [range]);

  useEffect(() => {
    setLoading(true);
    setError(null);
    getDns(filters)
      .then(r => { setRows(r.data); setTotal(r.total); })
      .catch(() => setError('Error conectando con el servicio.'))
      .finally(() => setLoading(false));
  }, [filters]);

  function applyFilters() {
    setFilters(f => ({
      ...f,
      page: 1,
      process: processInput || undefined,
      domain: domainInput || undefined,
      excludeTrusted: excludeTrusted || undefined,
      from: fromInput || undefined,
      to: toInput || undefined,
    }));
  }

  function clearFilters() {
    setProcessInput('');
    setDomainInput('');
    setExcludeTrusted(false);
    setFromInput('');
    setToInput('');
    setFilters({ page: 1, pageSize: 50 });
  }

  function toggleSuspicious() {
    const next = !excludeTrusted;
    setExcludeTrusted(next);
    setFilters(f => ({
      ...f,
      page: 1,
      process: processInput || undefined,
      domain: domainInput || undefined,
      excludeTrusted: next || undefined,
      from: fromInput || undefined,
      to: toInput || undefined,
    }));
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2 items-end">
        <input className="border rounded px-2 py-1 text-sm" placeholder="Proceso" value={processInput}
          onChange={e => setProcessInput(e.target.value)} onKeyDown={e => e.key === 'Enter' && applyFilters()} />
        <input className="border rounded px-2 py-1 text-sm" placeholder="Dominio (contiene)" value={domainInput}
          onChange={e => setDomainInput(e.target.value)} onKeyDown={e => e.key === 'Enter' && applyFilters()} />        <DateRangeFilter
          from={fromInput} to={toInput}
          onFromChange={setFromInput} onToChange={setToInput}
        />        <button className="bg-blue-600 text-white px-3 py-1 rounded text-sm hover:bg-blue-700" onClick={applyFilters}>Filtrar</button>
        <button className="border px-3 py-1 rounded text-sm hover:bg-gray-50" onClick={clearFilters}>
          Limpiar
        </button>
        {/* Toggle: solo sospechosos (excluir dominios de confianza) */}
        <button
          onClick={toggleSuspicious}
          className={`px-3 py-1 rounded text-sm border font-medium transition-colors ${
            excludeTrusted
              ? 'bg-amber-500 text-white border-amber-500'
              : 'bg-white text-gray-600 border-gray-300 hover:bg-gray-50'
          }`}
          title="Oculta dominios de la lista de confianza (Configuración > DNS)"
        >
          {excludeTrusted ? '🔍 Solo sospechosos' : '🔍 Mostrar solo sospechosos'}
        </button>
        <button
          className="ml-auto border border-green-600 text-green-700 px-3 py-1 rounded text-sm hover:bg-green-50"
          onClick={() => exportCsv(
            rows,
            [
              { key: 'timestamp', header: 'Timestamp' },
              { key: 'pid', header: 'PID' },
              { key: 'processName', header: 'Proceso' },
              { key: 'userName', header: 'Usuario' },
              { key: 'queryName', header: 'Dominio' },
              { key: 'queryResults', header: 'IPs resueltas' },
              { key: 'queryStatus', header: 'Estado' },
            ],
            'dns'
          )}
          title="Exportar página actual como CSV"
        >
          ↓ CSV
        </button>
      </div>

      {excludeTrusted && (
        <div className="text-xs text-amber-700 bg-amber-50 border border-amber-200 rounded px-3 py-1.5">
          Filtro activo: se ocultan los dominios marcados como de confianza en Configuración → DNS.
        </div>
      )}

      {error && <p className="text-red-600 text-sm">{error}</p>}
      {loading && <p className="text-gray-400 text-sm">Cargando...</p>}

      <div className="overflow-x-auto rounded border">
        <table className="min-w-full text-sm">
          <thead className="bg-gray-50 text-gray-600 uppercase text-xs">
            <tr>
              <th className="px-3 py-2 text-left">Timestamp</th>
              <th className="px-3 py-2 text-left">PID</th>
              <th className="px-3 py-2 text-left">Proceso</th>
              <th className="px-3 py-2 text-left">Usuario</th>
              <th className="px-3 py-2 text-left">Dominio</th>
              <th className="px-3 py-2 text-left">IPs resueltas</th>
              <th className="px-3 py-2 text-left">Estado</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {rows.map(r => (
              <tr key={r.id} className="hover:bg-gray-50">
                <td className="px-3 py-1.5"><Timestamp value={r.timestamp} /></td>
                <td className="px-3 py-1.5 font-mono text-xs">{r.pid}</td>
                <td className="px-3 py-1.5 font-medium">{r.processName}</td>
                <td className="px-3 py-1.5 text-gray-600">{r.userName}</td>
                <td className="px-3 py-1.5 font-semibold text-blue-700">{r.queryName}</td>
                <td className="px-3 py-1.5 font-mono text-xs text-gray-500">{r.queryResults}</td>
                <td className="px-3 py-1.5 text-gray-400 text-xs">{r.queryStatus}</td>
              </tr>
            ))}
            {!loading && rows.length === 0 && (
              <tr><td colSpan={7} className="px-3 py-4 text-center text-gray-400">Sin resultados</td></tr>
            )}
          </tbody>
        </table>
      </div>

      <Pagination page={filters.page} pageSize={filters.pageSize} total={total}
        onPageChange={p => setFilters(f => ({ ...f, page: p }))} />
    </div>
  );
}
