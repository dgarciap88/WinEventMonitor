/**
 * Descarga datos como archivo CSV en el navegador.
 * @param rows    Array de objetos a exportar
 * @param columns Columnas a incluir: { key, header }
 * @param filename Nombre del fichero (sin extensión)
 */
export function exportCsv<T extends object>(
  rows: T[],
  columns: { key: keyof T; header: string }[],
  filename: string
) {
  const escape = (v: unknown) => {
    const s = v == null ? '' : String(v);
    return s.includes(',') || s.includes('"') || s.includes('\n')
      ? `"${s.replace(/"/g, '""')}"`
      : s;
  };

  const header = columns.map(c => c.header).join(',');
  const lines  = rows.map(row =>
    columns.map(c => escape(row[c.key])).join(',')
  );

  const blob = new Blob(['\uFEFF' + [header, ...lines].join('\r\n')], {
    type: 'text/csv;charset=utf-8;',
  });

  const url = URL.createObjectURL(blob);
  const a   = document.createElement('a');
  a.href     = url;
  a.download = `${filename}_${new Date().toISOString().slice(0, 19).replace(/:/g, '-')}.csv`;
  a.click();
  URL.revokeObjectURL(url);
}
