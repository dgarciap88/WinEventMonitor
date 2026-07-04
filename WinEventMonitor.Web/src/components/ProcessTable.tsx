import { useState, useEffect } from 'react';
import { getProcesses } from '../api/client';
import type { ProcessEvent, ProcessFilters } from '../api/types';
import { exportCsv } from '../utils/exportCsv';
import { Timestamp } from './Timestamp';
import { ElevatedBadge } from './ElevatedBadge';
import { Pagination } from './Pagination';
import { DateRangeFilter } from './DateRangeFilter';
import { useDateRange } from '../context/DateRangeContext';

export function ProcessTable() {
  const { range } = useDateRange();
  const [filters, setFilters] = useState<ProcessFilters>({ page: 1, pageSize: 50 });
  const [rows, setRows] = useState<ProcessEvent[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [nameInput, setNameInput] = useState('');
  const [userInput, setUserInput] = useState('');
  const [elevatedInput, setElevatedInput] = useState('');
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
    getProcesses(filters)
      .then(r => { setRows(r.data); setTotal(r.total); })
      .catch(() => setError('Error conectando con el servicio. ¿Está corriendo como admin?'))
      .finally(() => setLoading(false));
  }, [filters]);

  function applyFilters() {
    setFilters(f => ({
      ...f,
      page: 1,
      name: nameInput || undefined,
      user: userInput || undefined,
      elevated: elevatedInput === '' ? undefined : elevatedInput === 'true',
      from: fromInput || undefined,
      to: toInput || undefined,
    }));
  }

  function clearFilters() {
    setNameInput(''); setUserInput(''); setElevatedInput('');
    setFromInput(''); setToInput('');
    setFilters({ page: 1, pageSize: 50 });
  }

  return (
    <div className="space-y-3">
      {/* Barra de filtros */}
      <div className="flex flex-wrap gap-2 items-center">
        <input
          className="border rounded px-2 py-1 text-sm"
          placeholder="Proceso (contiene)"
          value={nameInput}
          onChange={e => setNameInput(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && applyFilters()}
        />
        <input
          className="border rounded px-2 py-1 text-sm"
          placeholder="Usuario"
          value={userInput}
          onChange={e => setUserInput(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && applyFilters()}
        />
        <select
          className="border rounded px-2 py-1 text-sm"
          value={elevatedInput}
          onChange={e => setElevatedInput(e.target.value)}
        >
          <option value="">Todos</option>
          <option value="true">Solo Admin</option>
          <option value="false">Solo Normal</option>
        </select>
        <DateRangeFilter
          from={fromInput} to={toInput}
          onFromChange={setFromInput} onToChange={setToInput}
        />
        <button
          className="bg-blue-600 text-white px-3 py-1 rounded text-sm hover:bg-blue-700"
          onClick={applyFilters}
        >
          Filtrar
        </button>
        <button className="border px-3 py-1 rounded text-sm hover:bg-gray-50" onClick={clearFilters}>
          Limpiar
        </button>
        <button
          className="ml-auto border border-green-600 text-green-700 px-3 py-1 rounded text-sm hover:bg-green-50 flex items-center gap-1"
          onClick={() => exportCsv(
            rows,
            [
              { key: 'timestamp', header: 'Timestamp' },
              { key: 'eventType', header: 'Tipo' },
              { key: 'eventSource', header: 'Fuente' },
              { key: 'pid', header: 'PID' },
              { key: 'processName', header: 'Proceso' },
              { key: 'userName', header: 'Usuario' },
              { key: 'isElevated', header: 'Elevado' },
              { key: 'integrityLevel', header: 'Integridad' },
              { key: 'commandLine', header: 'Línea de comandos' },
            ],
            'procesos'
          )}
          title="Exportar página actual como CSV"
        >
          ↓ CSV
        </button>
      </div>

      {error && <p className="text-red-600 text-sm">{error}</p>}
      {loading && <p className="text-gray-400 text-sm">Cargando...</p>}

      {/* Tabla */}
      <div className="overflow-x-auto rounded border">
        <table className="min-w-full text-sm">
          <thead className="bg-gray-50 text-gray-600 uppercase text-xs">
            <tr>
              <th className="px-3 py-2 text-left">Timestamp</th>
              <th className="px-3 py-2 text-left">Tipo</th>
              <th className="px-3 py-2 text-left">Fuente</th>
              <th className="px-3 py-2 text-left">PID</th>
              <th className="px-3 py-2 text-left">Proceso</th>
              <th className="px-3 py-2 text-left">Usuario</th>
              <th className="px-3 py-2 text-left">Elevación</th>
              <th className="px-3 py-2 text-left">Integridad</th>
              <th className="px-3 py-2 text-left">Línea de comandos</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {rows.map(r => (
              <tr key={r.id} className="hover:bg-gray-50">
                <td className="px-3 py-1.5"><Timestamp value={r.timestamp} /></td>
                <td className="px-3 py-1.5">
                  <span className={r.eventType === 'Create' ? 'text-green-700' : 'text-gray-400'}>
                    {r.eventType === 'Create' ? '▶ Inicio' : '■ Fin'}
                  </span>
                </td>
                <td className="px-3 py-1.5 text-gray-500">{r.eventSource}</td>
                <td className="px-3 py-1.5 font-mono text-xs">{r.pid}</td>
                <td className="px-3 py-1.5 font-medium">{r.processName}</td>
                <td className="px-3 py-1.5 text-gray-600">{r.userName}</td>
                <td className="px-3 py-1.5"><ElevatedBadge elevated={r.isElevated} /></td>
                <td className="px-3 py-1.5 text-gray-500">{r.integrityLevel}</td>
                <td className="px-3 py-1.5 font-mono text-xs max-w-xs" title={r.commandLine ?? ''}>
                  <div className="flex items-center gap-1 group">
                    <span className="truncate">{r.commandLine}</span>
                    {r.commandLine && (
                      <button
                        className="opacity-0 group-hover:opacity-100 flex-shrink-0 text-gray-400 hover:text-blue-600 transition-opacity"
                        title="Copiar comando completo"
                        onClick={() => navigator.clipboard.writeText(r.commandLine!)}
                      >⎘</button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
            {!loading && rows.length === 0 && (
              <tr><td colSpan={9} className="px-3 py-4 text-center text-gray-400">Sin resultados</td></tr>
            )}
          </tbody>
        </table>
      </div>

      <Pagination
        page={filters.page}
        pageSize={filters.pageSize}
        total={total}
        onPageChange={p => setFilters(f => ({ ...f, page: p }))}
      />
    </div>
  );
}
