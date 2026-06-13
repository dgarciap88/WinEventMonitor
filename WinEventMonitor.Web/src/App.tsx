import { useState, useEffect } from 'react';
import { ProcessTable } from './components/ProcessTable';
import { NetworkTable } from './components/NetworkTable';
import { DnsTable } from './components/DnsTable';
import { SetupPanel } from './components/SetupPanel';
import { ProcessTreeView } from './components/ProcessTreeView';
import { AlertsPanel } from './components/AlertsPanel';
import { DashboardPanel } from './components/DashboardPanel';
import { SystemPanel } from './components/SystemPanel';
import { AccessPanel } from './components/AccessPanel';
import { ToastProvider } from './components/ToastProvider';
import { DateRangeProvider, DateRangeWidget } from './context/DateRangeContext';
import { GlobalSearch } from './components/GlobalSearch';
import { getAlertCount } from './api/client';

type Tab = 'dashboard' | 'system' | 'access' | 'processes' | 'network' | 'dns' | 'tree' | 'alerts' | 'setup';

const TABS: { id: Tab; label: string }[] = [
  { id: 'dashboard', label: '📊 Resumen' },
  { id: 'system',    label: '💻 Sistema' },
  { id: 'access',    label: '🔐 Accesos' },
  { id: 'processes', label: 'Procesos' },
  { id: 'network',   label: 'Red' },
  { id: 'dns',       label: 'DNS' },
  { id: 'tree',      label: 'Árbol' },
  { id: 'alerts',    label: 'Alertas' },
  { id: 'setup',     label: 'Configuración' },
];

export default function App() {
  const [tab, setTab] = useState<Tab>('dashboard');
  const [alertCount, setAlertCount] = useState(0);
  const [treeHighlightPid, setTreeHighlightPid] = useState<number | null>(null);
  const [searchOpen, setSearchOpen] = useState(false);

  const handleTabChange = (t: Tab) => {
    setTab(t);
    window.location.hash = t;
  };

  const navigateToTree = (pid: number) => {
    setTreeHighlightPid(pid);
    setTab('tree');
    window.location.hash = `tree?pid=${pid}`;
  };

  // Restaurar pestaña desde hash al montar
  useEffect(() => {
    const hash = window.location.hash.slice(1).split('?')[0] as Tab;
    if (hash && TABS.some(t => t.id === hash)) setTab(hash);
  }, []);

  // Ctrl+K abre búsqueda global
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault();
        setSearchOpen(v => !v);
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, []);

  // Badge de alertas: poll cada 30 s
  useEffect(() => {
    getAlertCount().then(setAlertCount).catch(() => {});
    const id = setInterval(() => {
      getAlertCount().then(setAlertCount).catch(() => {});
    }, 30_000);
    return () => clearInterval(id);
  }, []);

  return (
    <DateRangeProvider>
    <ToastProvider>
    <div className="min-h-screen bg-gray-50">
      <header className="bg-gray-900 text-white px-6 py-3 flex items-center gap-4">
        <span className="text-lg font-semibold tracking-tight">Windows Event Monitor</span>
        <span className="text-xs text-gray-400 bg-gray-800 px-2 py-0.5 rounded">MVP</span>
        <DateRangeWidget />
        <button
          onClick={() => setSearchOpen(true)}
          className="ml-auto text-xs text-gray-400 hover:text-gray-200 flex items-center gap-1.5 bg-gray-800 px-2.5 py-1 rounded hover:bg-gray-700 transition-colors"
          title="Búsqueda global (Ctrl+K)"
        >
          🔍 <kbd className="text-[10px] bg-gray-700 px-1 py-0.5 rounded font-sans">Ctrl+K</kbd>
        </button>
      </header>
      <nav className="bg-white border-b px-6 flex gap-1">
        {TABS.map(t => (
          <button
            key={t.id}
            onClick={() => handleTabChange(t.id)}
            className={"px-4 py-2.5 text-sm font-medium border-b-2 transition-colors " + (tab === t.id ? "border-blue-600 text-blue-600" : "border-transparent text-gray-500 hover:text-gray-800")}
          >
            {t.label}
            {t.id === 'alerts' && alertCount > 0 && (
              <span className="ml-1.5 px-1.5 py-0.5 rounded-full text-[10px] font-bold bg-red-500 text-white">
                {alertCount > 99 ? '99+' : alertCount}
              </span>
            )}
          </button>
        ))}
      </nav>
      <main className="px-6 py-4">
        {tab === 'dashboard'  && <DashboardPanel />}
        {tab === 'system'     && <SystemPanel />}
        {tab === 'access'     && <AccessPanel />}
        {tab === 'processes'  && <ProcessTable />}
        {tab === 'network'   && <NetworkTable />}
        {tab === 'dns'       && <DnsTable />}
        {tab === 'tree'      && <ProcessTreeView highlightPid={treeHighlightPid} onHighlightConsumed={() => setTreeHighlightPid(null)} />}
        {tab === 'alerts'    && <AlertsPanel onNavigateToTree={navigateToTree} />}
        {tab === 'setup'     && <SetupPanel />}
      </main>
    </div>
    {searchOpen && (
      <GlobalSearch
        onClose={() => setSearchOpen(false)}
        onNavigateToTab={t => { handleTabChange(t as Tab); setSearchOpen(false); }}
        onNavigateToTree={pid => { navigateToTree(pid); setSearchOpen(false); }}
      />
    )}
    </ToastProvider>
    </DateRangeProvider>
  );
}
