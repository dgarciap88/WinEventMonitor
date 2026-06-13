import { useState, useEffect } from 'react';
import { getNetwork } from '../api/client';
import type { NetworkEvent, NetworkFilters } from '../api/types';
import { exportCsv } from '../utils/exportCsv';
import { Timestamp } from './Timestamp';
import { Pagination } from './Pagination';
import { DateRangeFilter } from './DateRangeFilter';
import { useDateRange } from '../context/DateRangeContext';

export function NetworkTable() {
  const { range } = useDateRange();
  const [filters, setFilters] = useState<NetworkFilters>({ page: 1, pageSize: 50 });
  const [rows, setRows] = useState<NetworkEvent[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [processInput, setProcessInput] = useState('');
  const [destIpInput, setDestIpInput] = useState('');
  const [destPortInput, setDestPortInput] = useState('');
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
    getNetwork(filters)
      .then(r => { setRows(r.data); setTotal(r.total); })
      .catch(() => setError('Error conectando con el servicio.'))
      .finally(() => setLoading(false));
  }, [filters]);

  function applyFilters() {
    setFilters(f => ({
      ...f,
      page: 1,
      process: processInput || undefined,
      destIp: destIpInput || undefined,
      destPort: destPortInput ? Number(destPortInput) : undefined,
      from: fromInput || undefined,
      to: toInput || undefined,
    }));
  }

  function clearFilters() {
    setProcessInput(''); setDestIpInput(''); setDestPortInput('');
    setFromInput(''); setToInput('');
    setFilters({ page: 1, pageSize: 50 });
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2 items-center">
        <input className="border rounded px-2 py-1 text-sm" placeholder="Proceso" value={processInput}
          onChange={e => setProcessInput(e.target.value)} onKeyDown={e => e.key === 'Enter' && applyFilters()} />
        <input className="border rounded px-2 py-1 text-sm" placeholder="IP destino" value={destIpInput}
          onChange={e => setDestIpInput(e.target.value)} onKeyDown={e => e.key === 'Enter' && applyFilters()} />
        <input className="border rounded px-2 py-1 text-sm w-24" placeholder="Puerto" value={destPortInput}
          onChange={e => setDestPortInput(e.target.value)} onKeyDown={e => e.key === 'Enter' && applyFilters()} />
        <DateRangeFilter
          from={fromInput} to={toInput}
          onFromChange={setFromInput} onToChange={setToInput}
        />
        <button className="bg-blue-600 text-white px-3 py-1 rounded text-sm hover:bg-blue-700" onClick={applyFilters}>Filtrar</button>
        <button className="border px-3 py-1 rounded text-sm hover:bg-gray-50" onClick={clearFilters}>
          Limpiar
        </button>
        <button
          className="ml-auto border border-green-600 text-green-700 px-3 py-1 rounded text-sm hover:bg-green-50"
          onClick={() => exportCsv(
            rows,
            [
              { key: 'timestamp', header: 'Timestamp' },
              { key: 'pid', header: 'PID' },
              { key: 'processName', header: 'Proceso' },
              { key: 'executablePath', header: 'Ruta' },
              { key: 'userName', header: 'Usuario' },
              { key: 'protocol', header: 'Protocolo' },
              { key: 'sourceIp', header: 'IP Origen' },
              { key: 'sourcePort', header: 'Puerto Origen' },
              { key: 'destinationIp', header: 'IP Destino' },
              { key: 'destinationPort', header: 'Puerto Destino' },
            ],
            'red'
          )}
          title="Exportar página actual como CSV"
        >
          ↓ CSV
        </button>
      </div>

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
              <th className="px-3 py-2 text-left">Proto</th>
              <th className="px-3 py-2 text-left">Origen</th>
              <th className="px-3 py-2 text-left">Destino</th>
              <th className="px-3 py-2 text-left">Puerto dest.</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {rows.map(r => (
              <tr key={r.id} className="hover:bg-gray-50">
                <td className="px-3 py-1.5"><Timestamp value={r.timestamp} /></td>
                <td className="px-3 py-1.5 font-mono text-xs">{r.pid}</td>
                <td className="px-3 py-1.5">
                  <span className="font-medium block">{r.processName}</span>
                  {r.executablePath && (
                    <span
                      className="text-[10px] text-gray-400 block truncate max-w-[200px]"
                      title={r.executablePath}
                    >
                      {r.executablePath}
                    </span>
                  )}
                </td>
                <td className="px-3 py-1.5 text-gray-600">{r.userName}</td>
                <td className="px-3 py-1.5 text-gray-500">{r.protocol}</td>
                <td className="px-3 py-1.5 font-mono text-xs">{r.sourceIp}:{r.sourcePort}</td>
                <td className="px-3 py-1.5 font-mono text-xs font-semibold text-blue-700">{r.destinationIp}</td>
                <td className="px-3 py-1.5 font-mono text-xs">{r.destinationPort}</td>
              </tr>
            ))}
            {!loading && rows.length === 0 && (
              <tr><td colSpan={8} className="px-3 py-4 text-center text-gray-400">Sin resultados</td></tr>
            )}
          </tbody>
        </table>
      </div>

      <Pagination page={filters.page} pageSize={filters.pageSize} total={total}
        onPageChange={p => setFilters(f => ({ ...f, page: p }))} />
    </div>
  );
}
