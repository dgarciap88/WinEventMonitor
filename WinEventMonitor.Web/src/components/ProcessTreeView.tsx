import { useState, useEffect, useCallback, useRef } from 'react';
import { getLiveProcesses, getProcessTree, getAlertsByPid } from '../api/client';
import type { LiveProcess, ProcessTreeNode } from '../api/types';
import { ProcessTimelineModal } from './ProcessTimelineModal';

// ─── Tipos ───────────────────────────────────────────────────────────────────

type Mode = 'live' | 'db';
type DbHours = 1 | 6 | 24 | 168;

// ─── Heurísticas: procesos elevados sospechosos ───────────────────────────────

/** Rutas en las que un proceso legítimo del sistema NUNCA debería ejecutarse.
 * Se limitan a rutas escribibles por el usuario para evitar falsos positivos
 * con directorios Temp del sistema (p.ej. C:\ProgramData\...\Temp) */
const SUSPICIOUS_PATH_FRAGMENTS = [
  '\\appdata\\local\\temp\\',
  '\\appdata\\roaming\\',
  '\\users\\public\\',
  '\\downloads\\',
  '\\desktop\\',
  // C:\Windows\Temp sí es sospechoso para ejecutables de usuario
  'c:\\windows\\temp\\',
];

/** Procesos del sistema que, por su naturaleza, pueden no tener executablePath
 * visible desde WMI (procesos protegidos o de sesión System). No son sospechosos. */
const KNOWN_SAFE_NO_PATH = new Set([
  'searchprotocolhost.exe', 'searchindexer.exe', 'searchfilterhost.exe',
  'sihost.exe', 'fontdrvhost.exe', 'audiodg.exe', 'dwm.exe',
  'registry', 'memory compression', 'system', 'secure system',
  'wininit.exe', 'csrss.exe', 'smss.exe', 'lsaiso.exe',
]);

/** Nombres de proceso del sistema que DEBEN venir de C:\\Windows\\ */
const SYSTEM_PROC_NAMES = new Set([
  'svchost.exe', 'lsass.exe', 'services.exe', 'csrss.exe',
  'wininit.exe', 'winlogon.exe', 'smss.exe', 'explorer.exe',
  'taskhostw.exe', 'spoolsv.exe', 'lsm.exe',
]);

/** Aplicaciones que casi nunca deben ejecutarse elevadas */
const RARELY_ELEVATED = new Set([
  'chrome.exe', 'firefox.exe', 'msedge.exe', 'iexplore.exe',
  'opera.exe', 'brave.exe',
  'winword.exe', 'excel.exe', 'powerpnt.exe', 'outlook.exe',
  'acrord32.exe', 'acrobat.exe',
]);

/**
 * Devuelve una lista de razones por las que un proceso elevado podría ser sospechoso.
 * Lista vacía → sin indicadores de riesgo.
 */
function getSuspicionReasons(proc: LiveProcess): string[] {
  const reasons: string[] = [];
  const name = proc.name.toLowerCase();
  const path = (proc.executablePath ?? '').toLowerCase();

  // 1. Proceso del sistema corriendo fuera de C:\Windows\
  if (SYSTEM_PROC_NAMES.has(name) && path && !path.startsWith('c:\\windows\\')) {
    reasons.push(`Masquerade: "${proc.name}" fuera de C:\\Windows\\ → ${proc.executablePath}`);
  }

  // 2. Elevado desde ruta de escritura de usuario (temp, downloads…)
  if (proc.isElevated) {
    for (const frag of SUSPICIOUS_PATH_FRAGMENTS) {
      if (path.includes(frag)) {
        reasons.push(`Elevado desde ruta sospechosa: ${proc.executablePath}`);
        break;
      }
    }
  }

  // 3. Browser u ofimática ejecutando con privilegios elevados
  if (proc.isElevated && RARELY_ELEVATED.has(name)) {
    reasons.push(`"${proc.name}" corriendo elevado (inusual para este proceso)`);
  }

  // 4. Shell elevada sin hash SHA256 (no capturada por Sysmon al crear)
  if (proc.isElevated && !proc.sha256 &&
      (name === 'cmd.exe' || name === 'powershell.exe' || name === 'pwsh.exe')) {
    reasons.push('Shell elevada sin hash SHA256 — Sysmon no capturó su creación');
  }

  // 5. Elevado sin ruta ejecutable visible (posible process hollowing)
  // Se excluyen procesos del sistema conocidos que legítimamente no exponen su ruta
  if (proc.isElevated && !proc.executablePath && !KNOWN_SAFE_NO_PATH.has(name)) {
    reasons.push('Proceso elevado sin ruta ejecutable visible (posible process hollowing)');
  }

  // 6. Integridad System en proceso fuera de rutas del sistema
  const isSysPath = path.startsWith('c:\\windows\\') || path.startsWith('c:\\program files\\');
  if (proc.integrityLevel === 'System' && path && !isSysPath) {
    reasons.push('Integridad "System" en proceso fuera de rutas del sistema');
  }

  return reasons;
}

// ─── Filtrado recursivo del árbol ────────────────────────────────────────────

/**
 * Mantiene un nodo si él mismo cumple los criterios O algún descendiente los cumple.
 * Esto preserva el contexto del árbol (se ven los padres de procesos que coinciden).
 */
function filterTree(
  nodes: ProcessTreeNode[],
  nameLower: string,
  onlyElevated: boolean,
  onlySuspicious: boolean
): ProcessTreeNode[] {
  return nodes.reduce<ProcessTreeNode[]>((acc, node) => {
    const filteredChildren = filterTree(node.children, nameLower, onlyElevated, onlySuspicious);
    const nameOk = !nameLower ||
      node.name.toLowerCase().includes(nameLower) ||
      (node.commandLine?.toLowerCase().includes(nameLower) ?? false) ||
      String(node.pid) === nameLower.trim();
    const elevatedOk = !onlyElevated || node.isElevated;
    const suspiciousOk = !onlySuspicious || getSuspicionReasons(node).length > 0;
    const selfMatches = nameOk && elevatedOk && suspiciousOk;
    if (selfMatches || filteredChildren.length > 0) {
      acc.push({ ...node, children: filteredChildren });
    }
    return acc;
  }, []);
}

// ─── Construcción del árbol ──────────────────────────────────────────────────

function buildTree(items: LiveProcess[]): ProcessTreeNode[] {
  // En histórico puede haber múltiples arranques del mismo PID en la ventana.
  // Si no deduplicamos, se pueden crear ciclos al enlazar parentPid.
  const uniqueByPid = new Map<number, LiveProcess>();
  for (const item of items) {
    uniqueByPid.set(item.pid, item);
  }
  const uniqueItems = Array.from(uniqueByPid.values());

  const map = new Map<number, ProcessTreeNode>();
  const roots: ProcessTreeNode[] = [];

  for (const item of uniqueItems) {
    map.set(item.pid, { ...item, children: [] });
  }

  for (const item of uniqueItems) {
    const node = map.get(item.pid)!;
    const parent = item.parentPid ? map.get(item.parentPid) : undefined;
    if (parent && parent.pid !== node.pid) {
      parent.children.push(node);
    } else {
      roots.push(node);
    }
  }

  return roots;
}

/** Recorre el árbol y devuelve los PIDs de todos los nodos que tienen hijos. */
function collectParentPids(nodes: ProcessTreeNode[]): Set<number> {
  const pids = new Set<number>();
  const visited = new Set<number>();

  function walk(list: ProcessTreeNode[]) {
    for (const n of list) {
      if (visited.has(n.pid)) continue;
      visited.add(n.pid);

      if (n.children.length > 0) {
        pids.add(n.pid);
        walk(n.children);
      }
    }
  }
  walk(nodes);
  return pids;
}

// ─── Fila del árbol ──────────────────────────────────────────────────────────

interface ProcessRowProps {
  node: ProcessTreeNode;
  depth: number;
  expandedPids: Set<number>;
  togglePid: (pid: number) => void;
  highlightedPid?: number | null;
  alertsByPid?: Record<number, number>;
  onOpenTimeline?: (pid: number, name: string) => void;
}

function ProcessRow({ node, depth, expandedPids, togglePid, highlightedPid, alertsByPid, onOpenTimeline }: ProcessRowProps) {
  const isExpanded = expandedPids.has(node.pid);
  const hasChildren = node.children.length > 0;

  const [showReasons, setShowReasons] = useState(false);

  const elevated = node.isElevated;
  const suspicionReasons = getSuspicionReasons(node);
  const isSuspicious = suspicionReasons.length > 0;

  const rowClass = node.pid === highlightedPid
    ? 'bg-blue-100 hover:bg-blue-200 ring-1 ring-blue-400'
    : isSuspicious
      ? 'bg-orange-50 hover:bg-orange-100'
      : elevated
        ? 'bg-red-50 hover:bg-red-100'
        : 'hover:bg-gray-50';

  const nameClass = isSuspicious
    ? 'text-orange-700 font-semibold'
    : elevated
      ? 'text-red-700 font-medium'
      : 'text-gray-800';

  const truncate = (s: string | null, max = 60) =>
    s && s.length > max ? s.slice(0, max) + '…' : (s ?? '—');

  return (
    <>
      <tr className={`border-b border-gray-100 text-xs ${rowClass}`} data-pid={node.pid}>
        {/* Nombre + árbol */}
        <td className="py-1 pr-2" style={{ paddingLeft: depth * 16 + 8 }}>
          <span className="flex items-center gap-1 min-w-0">
            {hasChildren ? (
              <button
                onClick={() => togglePid(node.pid)}
                className="text-gray-400 hover:text-gray-700 w-4 text-center flex-shrink-0 font-mono"
                title={isExpanded ? 'Colapsar' : 'Expandir'}
              >
                {isExpanded ? '▼' : '▶'}
              </button>
            ) : (
              <span className="w-4 flex-shrink-0" />
            )}
            <span className={`truncate ${nameClass}`} title={node.name}>
              {node.name}
            </span>
            {isSuspicious && (
              <button
                onClick={() => setShowReasons(v => !v)}
                className={`ml-1 px-1 py-0 rounded text-[10px] flex-shrink-0 transition-colors ${
                  showReasons
                    ? 'bg-orange-500 text-white'
                    : 'bg-orange-200 text-orange-800 hover:bg-orange-300'
                }`}
                title="Ver motivos de sospecha"
              >
                {showReasons ? '⚠ ocultar' : '⚠ sospechoso'}
              </button>
            )}
            {elevated && !isSuspicious && (
              <span className="ml-1 px-1 py-0 rounded text-[10px] bg-red-200 text-red-700 flex-shrink-0">
                elevado
              </span>
            )}
            {node.cpuPercent != null && node.cpuPercent > 0.5 && (
              <span className={`ml-1 px-1 py-0 rounded text-[10px] font-mono flex-shrink-0 ${
                node.cpuPercent > 50 ? 'bg-red-100 text-red-600' :
                node.cpuPercent > 20 ? 'bg-yellow-100 text-yellow-700' :
                'bg-blue-100 text-blue-600'}`}
              >
                {node.cpuPercent.toFixed(1)}%
              </span>
            )}
            {node.workingSetMb != null && node.workingSetMb > 0 && (
              <span className="ml-0.5 px-1 py-0 rounded text-[10px] bg-gray-100 text-gray-500 font-mono flex-shrink-0">
                {node.workingSetMb > 1024
                  ? `${(node.workingSetMb / 1024).toFixed(1)}GB`
                  : `${node.workingSetMb}MB`}
              </span>
            )}
            {(alertsByPid?.[node.pid] ?? 0) > 0 && (
              <span
                className="ml-1 px-1 py-0 rounded text-[10px] bg-red-500 text-white font-bold flex-shrink-0 cursor-pointer hover:bg-red-600"
                title={`${alertsByPid![node.pid]} alerta(s)`}
                onClick={e => { e.stopPropagation(); onOpenTimeline?.(node.pid, node.name); }}
              >
                🔴{alertsByPid![node.pid]}
              </span>
            )}
          </span>
        </td>
        {/* PID */}
        <td className="px-2 py-1 text-gray-500 tabular-nums">{node.pid}</td>
        {/* PPID */}
        <td className="px-2 py-1 text-gray-400 tabular-nums">{node.parentPid || '—'}</td>
        {/* Usuario */}
        <td className="px-2 py-1 text-gray-500 max-w-[120px] truncate">
          {node.userName ?? '—'}
        </td>
        {/* Integridad */}
        <td className="px-2 py-1">
          <IntegrityBadge level={node.integrityLevel} />
        </td>
        {/* Ruta ejecutable + CommandLine */}
        <td
          className="px-2 py-1 text-gray-400 max-w-xs"
          title={[node.executablePath, node.commandLine].filter(Boolean).join('\n')}
        >
          {node.executablePath && (
            <span className="block truncate text-gray-500 font-mono" style={{fontSize:'10px'}}>
              {truncate(node.executablePath, 55)}
            </span>
          )}
          {node.commandLine && (
            <span className="block truncate text-gray-400" style={{fontSize:'10px'}}>
              {truncate(node.commandLine, 55)}
            </span>
          )}
          {!node.executablePath && !node.commandLine && '\u2014'}
        </td>
        {/* Hash SHA256 */}
        <td className="px-2 py-1 text-gray-300 font-mono max-w-[80px] truncate" title={node.sha256 ?? ''}>
          {node.sha256 ? node.sha256.slice(0, 8) + '…' : '—'}
        </td>
        {/* Timeline */}
        <td className="px-2 py-1">
          <button
            onClick={e => { e.stopPropagation(); onOpenTimeline?.(node.pid, node.name); }}
            className="text-gray-300 hover:text-blue-500 text-xs transition-colors"
            title="Ver timeline del proceso"
          >
            📋
          </button>
        </td>
      </tr>

      {isSuspicious && showReasons && (
        <tr className="bg-orange-50 border-b border-orange-100">
          <td colSpan={7} style={{ paddingLeft: depth * 16 + 32 }} className="pb-2 pt-1 pr-4">
            <div className="text-[11px] text-orange-800 space-y-0.5">
              <span className="font-semibold block mb-1">Indicadores de riesgo:</span>
              {suspicionReasons.map((r, i) => (
                <div key={i} className="flex items-start gap-1">
                  <span className="text-orange-400 flex-shrink-0">•</span>
                  <span>{r}</span>
                </div>
              ))}
            </div>
          </td>
        </tr>
      )}

      {isExpanded &&
        node.children.map((child) => (
          <ProcessRow
            key={`${child.pid}-${child.name}`}
            node={child}
            depth={depth + 1}
            expandedPids={expandedPids}
            togglePid={togglePid}
            highlightedPid={highlightedPid}
            alertsByPid={alertsByPid}
            onOpenTimeline={onOpenTimeline}
          />
        ))}
    </>
  );
}

// ─── Badge de integridad ─────────────────────────────────────────────────────

function IntegrityBadge({ level }: { level: string | null }) {
  if (!level) return <span className="text-gray-300">—</span>;
  const colors: Record<string, string> = {
    System:    'bg-purple-100 text-purple-700',
    High:      'bg-orange-100 text-orange-700',
    Medium:    'bg-yellow-100 text-yellow-700',
    Low:       'bg-blue-100 text-blue-700',
    Untrusted: 'bg-red-100 text-red-700',
  };
  const cls = colors[level] ?? 'bg-gray-100 text-gray-600';
  return (
    <span className={`px-1.5 py-0 rounded text-[10px] font-medium ${cls}`}>
      {level}
    </span>
  );
}

/** Cuenta el total de nodos en un subárbol. */
function countNodes(node: ProcessTreeNode): number {
  return 1 + node.children.reduce((n, c) => n + countNodes(c), 0);
}

// ─── Componente principal ────────────────────────────────────────────────────

export function ProcessTreeView({
  highlightPid,
  onHighlightConsumed,
}: {
  highlightPid?: number | null;
  onHighlightConsumed?: () => void;
}) {
  const [mode, setMode] = useState<Mode>('live');
  const [dbHours, setDbHours] = useState<DbHours>(1);
  const [items, setItems] = useState<LiveProcess[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const [expandedPids, setExpandedPids] = useState<Set<number>>(new Set());
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const tableContainerRef = useRef<HTMLDivElement | null>(null);

  // ── Filtros ──────────────────────────────────────────────────────────────
  const [nameFilter, setNameFilter] = useState('');
  const [onlyElevated, setOnlyElevated] = useState(false);
  const [onlySuspicious, setOnlySuspicious] = useState(false);
  // PID destacado desde correlación alerta→árbol
  const [highlightedPid, setHighlightedPid] = useState<number | null>(null);

  // Mapa pid → número de alertas
  const [alertsByPid, setAlertsByPid] = useState<Record<number, number>>({});
  const [timelineProcess, setTimelineProcess] = useState<{ pid: number; name: string } | null>(null);

  useEffect(() => {
    getAlertsByPid().then(setAlertsByPid).catch(() => {});
    const id = setInterval(() => {
      getAlertsByPid().then(setAlertsByPid).catch(() => {});
    }, 60_000);
    return () => clearInterval(id);
  }, []);

  // Cuando llega un highlightPid externo, lo almacenamos y filtramos por él
  useEffect(() => {
    if (highlightPid != null) {
      setHighlightedPid(highlightPid);
      setNameFilter(String(highlightPid));
      onHighlightConsumed?.();
    }
  }, [highlightPid, onHighlightConsumed]);

  // Scroll al pid destacado después de que el árbol se renderice
  useEffect(() => {
    if (highlightedPid == null || !tableContainerRef.current) return;
    const row = tableContainerRef.current.querySelector(`[data-pid="${highlightedPid}"]`);
    if (row) row.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }, [highlightedPid, items]);

  // ── Inicialización expandedPids al cargar datos nuevos ──────────────────
  const setDataAndExpand = useCallback((data: LiveProcess[]) => {
    setItems(data);
    setLastUpdated(new Date());
    setExpandedPids((prev) => {
      // Expandir raíces que aún no están en el estado (primera carga)
      if (prev.size === 0) {
        const tree = buildTree(data);
        return collectParentPids(tree);
      }
      return prev;
    });
  }, []);

  // ── Fetch de datos ───────────────────────────────────────────────────────
  const fetchData = useCallback(async () => {
    try {
      setError(null);
      const data = mode === 'live'
        ? await getLiveProcesses()
        : await getProcessTree(dbHours);
      setDataAndExpand(data);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e);
      setError(msg);
    } finally {
      setLoading(false);
    }
  }, [mode, dbHours, setDataAndExpand]);

  // ── Arranque / reinicio del polling ─────────────────────────────────────
  useEffect(() => {
    setLoading(true);
    setItems([]);
    setExpandedPids(new Set());
    fetchData();

    if (mode === 'live') {
      timerRef.current = setInterval(fetchData, 5000);
    }

    return () => {
      if (timerRef.current) clearInterval(timerRef.current);
    };
  }, [mode, dbHours]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Toggle expand/collapse ───────────────────────────────────────────────
  const togglePid = useCallback((pid: number) => {
    setExpandedPids((prev) => {
      const next = new Set(prev);
      if (next.has(pid)) next.delete(pid);
      else next.add(pid);
      return next;
    });
  }, []);

  // ── Expandir / colapsar todo ─────────────────────────────────────────────
  const expandAll = () => {
    const tree = buildTree(items);
    setExpandedPids(collectParentPids(tree));
  };
  const collapseAll = () => setExpandedPids(new Set());

  // ── Árbol renderizable (con filtros aplicados) ───────────────────────────
  const rawTree = buildTree(items);
  const tree = filterTree(rawTree, nameFilter.toLowerCase().trim(), onlyElevated, onlySuspicious);

  // ── Stats ────────────────────────────────────────────────────────────────
  const elevatedCount = items.filter((p) => p.isElevated).length;
  const suspiciousCount = items.filter((p) => getSuspicionReasons(p).length > 0).length;

  return (
    <div className="space-y-3">
      {/* ── Controles ── */}
      <div className="flex flex-wrap items-center gap-3">
        {/* Toggle modo */}
        <div className="flex rounded-lg border border-gray-200 overflow-hidden text-sm">
          <button
            onClick={() => { setMode('live'); }}
            className={`px-4 py-1.5 font-medium transition-colors ${
              mode === 'live'
                ? 'bg-green-600 text-white'
                : 'bg-white text-gray-600 hover:bg-gray-50'
            }`}
          >
            🟢 En vivo
          </button>
          <button
            onClick={() => { setMode('db'); }}
            className={`px-4 py-1.5 font-medium transition-colors border-l border-gray-200 ${
              mode === 'db'
                ? 'bg-blue-600 text-white'
                : 'bg-white text-gray-600 hover:bg-gray-50'
            }`}
          >
            📊 Histórico
          </button>
        </div>

        {/* Selector de horas (solo modo DB) */}
        {mode === 'db' && (
          <div className="flex rounded-lg border border-gray-200 overflow-hidden text-sm">
            {([1, 6, 24, 168] as DbHours[]).map((h) => (
              <button
                key={h}
                onClick={() => setDbHours(h)}
                className={`px-3 py-1.5 border-l border-gray-200 first:border-l-0 transition-colors ${
                  dbHours === h
                    ? 'bg-blue-100 text-blue-700 font-semibold'
                    : 'bg-white text-gray-500 hover:bg-gray-50'
                }`}
              >
                {h === 168 ? '7d' : `${h}h`}
              </button>
            ))}
          </div>
        )}

        {/* Acciones árbol */}
        <div className="flex gap-1 text-xs text-gray-500">
          <button onClick={expandAll} className="px-2 py-1 rounded border border-gray-200 hover:bg-gray-100">
            Expandir todo
          </button>
          <button onClick={collapseAll} className="px-2 py-1 rounded border border-gray-200 hover:bg-gray-100">
            Colapsar todo
          </button>
        </div>

        {/* Actualizar (modo DB) */}
        {mode === 'db' && (
          <button
            onClick={() => { setLoading(true); fetchData(); }}
            className="px-3 py-1.5 text-sm rounded border border-gray-200 hover:bg-gray-50 text-gray-600"
          >
            ↺ Actualizar
          </button>
        )}

        {/* Stats */}
        <span className="ml-auto text-xs text-gray-400">
          {items.length} procesos
          {elevatedCount > 0 && (
            <span className="ml-2 text-red-500">{elevatedCount} elevados</span>
          )}
          {suspiciousCount > 0 && (
            <span className="ml-2 text-orange-600 font-semibold">⚠ {suspiciousCount} sospechosos</span>
          )}
          {mode === 'live' && lastUpdated && (
            <span className="ml-2">
              · actualizado {lastUpdated.toLocaleTimeString()}
            </span>
          )}
        </span>
      </div>

      {/* ── Filtros ── */}
      <div className="flex flex-wrap gap-2 items-center">
        <input
          type="text"
          value={nameFilter}
          onChange={e => setNameFilter(e.target.value)}
          placeholder="Filtrar por nombre o comando…"
          className="border rounded px-2 py-1 text-sm w-64"
        />
        <button
          onClick={() => setOnlyElevated(v => !v)}
          className={`px-3 py-1 rounded text-sm border font-medium transition-colors ${
            onlyElevated
              ? 'bg-red-500 text-white border-red-500'
              : 'bg-white text-gray-600 border-gray-300 hover:bg-gray-50'
          }`}
        >
          {onlyElevated ? '🔴 Solo elevados' : 'Solo elevados'}
        </button>
        <button
          onClick={() => setOnlySuspicious(v => !v)}
          className={`px-3 py-1 rounded text-sm border font-medium transition-colors ${
            onlySuspicious
              ? 'bg-orange-500 text-white border-orange-500'
              : 'bg-white text-gray-600 border-gray-300 hover:bg-gray-50'
          }`}
          title="Muestra solo procesos con indicadores de comportamiento sospechoso"
        >
          {onlySuspicious ? '⚠ Solo sospechosos' : '⚠ Solo sospechosos'}
        </button>
        {(nameFilter || onlyElevated || onlySuspicious) && (
          <button
            onClick={() => { setNameFilter(''); setOnlyElevated(false); setOnlySuspicious(false); }}
            className="text-xs text-gray-400 hover:text-gray-600 px-2 py-1 border rounded"
          >
            ✕ Limpiar filtros
          </button>
        )}
        {(nameFilter || onlyElevated || onlySuspicious) && (
          <span className="text-xs text-gray-400">
            {tree.reduce((n, node) => n + countNodes(node), 0)} resultados
          </span>
        )}
      </div>

      {/* ── Error ── */}
      {error && (
        <div className="text-sm text-red-600 bg-red-50 border border-red-200 rounded px-3 py-2">
          Error al cargar procesos: {error}
        </div>
      )}

      {/* ── Tabla ── */}
      <div ref={tableContainerRef} className="rounded-lg border border-gray-200 bg-white overflow-auto max-h-[calc(100vh-200px)]">
        {loading && items.length === 0 ? (
          <div className="py-12 text-center text-sm text-gray-400">Cargando procesos…</div>
        ) : tree.length === 0 ? (
          <div className="py-12 text-center text-sm text-gray-400">
            {mode === 'db' ? 'No hay eventos en la ventana seleccionada.' : 'Sin procesos.'}
          </div>
        ) : (
          <table className="w-full table-fixed text-xs">
            <thead className="sticky top-0 bg-gray-50 border-b border-gray-200 z-10">
              <tr>
                <th className="text-left px-2 py-2 font-semibold text-gray-600 w-64">Proceso</th>
                <th className="text-left px-2 py-2 font-semibold text-gray-600 w-16">PID</th>
                <th className="text-left px-2 py-2 font-semibold text-gray-600 w-16">PPID</th>
                <th className="text-left px-2 py-2 font-semibold text-gray-600 w-32">Usuario</th>
                <th className="text-left px-2 py-2 font-semibold text-gray-600 w-24">Integridad</th>
                <th className="text-left px-2 py-2 font-semibold text-gray-600">Ruta / Comando</th>
                <th className="text-left px-2 py-2 font-semibold text-gray-600 w-24">SHA256</th>
                <th className="text-left px-2 py-2 font-semibold text-gray-600 w-8"></th>
              </tr>
            </thead>
            <tbody>
              {tree.map((node) => (
                <ProcessRow
                  key={`${node.pid}-${node.name}`}
                  node={node}
                  depth={0}
                  expandedPids={expandedPids}
                  togglePid={togglePid}
                  highlightedPid={highlightedPid}
                  alertsByPid={alertsByPid}
                  onOpenTimeline={(pid, name) => setTimelineProcess({ pid, name })}
                />
              ))}
            </tbody>
          </table>
        )}
      </div>

      {timelineProcess && (
        <ProcessTimelineModal
          pid={timelineProcess.pid}
          processName={timelineProcess.name}
          onClose={() => setTimelineProcess(null)}
        />
      )}
    </div>
  );
}
