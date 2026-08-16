function escapeCsvField(v: unknown): string {
  const s = v == null ? '' : String(v);
  return s.includes(',') || s.includes('"') || s.includes('\n')
    ? `"${s.replace(/"/g, '""')}"`
    : s;
}

/**
 * Construye el contenido CSV (sin BOM) a partir de filas y columnas.
 * Separado de exportCsv para poder testearlo sin depender del DOM.
 */
export function toCsvString<T extends object>(
  rows: T[],
  columns: { key: keyof T; header: string }[]
): string {
  const header = columns.map(c => c.header).join(',');
  const lines  = rows.map(row =>
    columns.map(c => escapeCsvField(row[c.key])).join(',')
  );
  return [header, ...lines].join('\r\n');
}

/**
 * Descarga datos como archivo CSV en el navegador.
 * @param rows    Array de objetos a exportar
 * @param columns Columnas a incluir: { key, header }
 * @param filename Nombre del fichero (sin extensi\u00F3n)
 */
export function exportCsv<T extends object>(
  rows: T[],
  columns: { key: keyof T; header: string }[],
  filename: string
) {
  const blob = new Blob(['\uFEFF' + toCsvString(rows, columns)], {
    type: 'text/csv;charset=utf-8;',
  });

  const url = URL.createObjectURL(blob);
  const a   = document.createElement('a');
  a.href     = url;
  a.download = `${filename}_${new Date().toISOString().slice(0, 19).replace(/:/g, '-')}.csv`;
  a.click();
  URL.revokeObjectURL(url);
}
