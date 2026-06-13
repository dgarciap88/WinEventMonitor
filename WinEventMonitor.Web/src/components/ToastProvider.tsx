import { createContext, useContext, useState, useCallback, useEffect, useRef } from 'react';
import { getAlerts } from '../api/client';

// ─── Tipos ─────────────────────────────────────────────────────────────────

interface Toast {
  id: string;
  severity: 'High' | 'Medium';
  title: string;
  body: string;
}

interface ToastCtx {
  toasts: Toast[];
  dismiss: (id: string) => void;
}

const Ctx = createContext<ToastCtx>({ toasts: [], dismiss: () => {} });

export const useToasts = () => useContext(Ctx);

// ─── Provider ─────────────────────────────────────────────────────────────

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  // IDs de alertas ya mostradas (para no repetir entre polls)
  const seenRef = useRef<Set<string>>(new Set());
  // Primera carga: silenciosa (no notificar alertas históricas)
  const initialLoadDone = useRef(false);

  const dismiss = useCallback((id: string) => {
    setToasts(t => t.filter(x => x.id !== id));
  }, []);

  const poll = useCallback(async () => {
    try {
      const result = await getAlerts(1, 20);
      const news: Toast[] = [];

      for (const a of result.data.filter(x => x.severity === 'High')) {
        if (seenRef.current.has(a.id)) continue;
        seenRef.current.add(a.id);
        if (!initialLoadDone.current) continue; // silenciar históricos
        news.push({
          id:       a.id,
          severity: 'High',
          title:    `⚠ Alerta ${a.severity}: ${a.rule}`,
          body:     a.description,
        });
      }

      initialLoadDone.current = true;

      if (news.length > 0) {
        setToasts(t => [...news, ...t].slice(0, 5));
        // Auto-dismiss después de 8 s
        news.forEach(n => setTimeout(() => dismiss(n.id), 8_000));
      }
    } catch { /* silencioso */ }
  }, [dismiss]);

  useEffect(() => {
    poll();
    const id = setInterval(poll, 15_000);
    return () => clearInterval(id);
  }, [poll]);

  return (
    <Ctx.Provider value={{ toasts, dismiss }}>
      {children}
      <ToastContainer toasts={toasts} dismiss={dismiss} />
    </Ctx.Provider>
  );
}

// ─── Contenedor de toasts ─────────────────────────────────────────────────

function ToastContainer({ toasts, dismiss }: { toasts: Toast[]; dismiss: (id: string) => void }) {
  if (toasts.length === 0) return null;

  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm">
      {toasts.map(t => (
        <div
          key={t.id}
          className="bg-white border-l-4 border-red-500 rounded-lg shadow-xl px-4 py-3 flex items-start gap-3 animate-in slide-in-from-right-4"
          role="alert"
        >
          <div className="flex-1 min-w-0">
            <p className="text-sm font-semibold text-gray-900 truncate">{t.title}</p>
            <p className="text-xs text-gray-600 mt-0.5 line-clamp-2">{t.body}</p>
          </div>
          <button
            onClick={() => dismiss(t.id)}
            className="text-gray-400 hover:text-gray-600 text-lg leading-none shrink-0 mt-0.5"
          >
            ×
          </button>
        </div>
      ))}
    </div>
  );
}
