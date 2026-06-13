import { useState, useEffect, useCallback } from 'react';
import { getSetupStatus, applySysmonConfig, enableAuditPolicy, enableLogonAudit, getTrustedDomains, addTrustedDomain, removeTrustedDomain, getAlertRules, patchAlertRule } from '../api/client';
import type { SetupStatus, AlertRuleConfig } from '../api/types';

function StatusBadge({ ok, label }: { ok: boolean; label: string }) {
  return (
    <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${ok ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>
      {label}
    </span>
  );
}

export function SetupPanel() {
  const [status, setStatus]   = useState<SetupStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [applying, setApplying] = useState<string | null>(null);
  const [message, setMessage] = useState<{ text: string; ok: boolean } | null>(null);

  // Dominios DNS de confianza
  const [trustedDomains, setTrustedDomains] = useState<string[]>([]);
  const [newDomain, setNewDomain] = useState('');
  const [domainBusy, setDomainBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [s, domains] = await Promise.all([getSetupStatus(), getTrustedDomains()]);
      setStatus(s);
      setTrustedDomains(domains);
    } catch {
      setMessage({ text: 'No se puede conectar con el servicio backend. ¿Está arrancado como administrador?', ok: false });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const handle = async (action: () => Promise<{ message: string }>, key: string) => {
    setApplying(key);
    setMessage(null);
    try {
      const res = await action();
      setMessage({ text: res.message, ok: true });
      await load();
    } catch (e: unknown) {
      const err = e as { response?: { status?: number; data?: { detail?: string; title?: string } } };
      const status = err?.response?.status;
      if (status === 404) {
        setMessage({ text: 'Endpoint no encontrado (404). El servicio está corriendo con código antiguo — necesitas pararlo, recompilar y arrancarlo de nuevo.', ok: false });
      } else if (status === 500) {
        setMessage({ text: err?.response?.data?.detail ?? 'Error interno del servicio (500). Revisa que el servicio corre como Administrador.', ok: false });
      } else {
        setMessage({ text: err?.response?.data?.detail ?? `Error desconocido${status ? ` (HTTP ${status})` : ''}`, ok: false });
      }
    } finally {
      setApplying(null);
    }
  };

  if (loading) return <p className="text-sm text-gray-500 py-8 text-center">Cargando estado del sistema...</p>;

  if (!status) return null;

  const { sysmon, auditPolicy, storage } = status;

  return (
    <div className="max-w-2xl space-y-4">

      {/* Mensaje de resultado */}
      {message && (
        <div className={`text-sm px-4 py-3 rounded border ${message.ok
          ? 'bg-green-50 border-green-200 text-green-800'
          : 'bg-red-50 border-red-200 text-red-800'}`}>
          {message.text}
        </div>
      )}

      {/* ─── Tarjeta Sysmon ─── */}
      <div className="bg-white border rounded-lg overflow-hidden">
        <div className="px-4 py-3 bg-gray-50 border-b flex items-center gap-2">
          <span className="font-semibold text-sm text-gray-800">Sysmon</span>
          <StatusBadge ok={sysmon.serviceRunning}
            label={sysmon.serviceRunning ? 'Servicio activo' : 'Servicio inactivo'} />
          {sysmon.configApplied &&
            <StatusBadge ok={true} label="Config MVP aplicada" />}
        </div>

        <dl className="px-4 py-3 space-y-2 text-sm text-gray-600">
          <div className="flex justify-between items-center">
            <dt>Ejecutable</dt>
            <dd>
              {sysmon.executableFound
                ? <code className="text-xs text-gray-500">{sysmon.executablePath}</code>
                : <span className="text-red-600 font-medium">No encontrado</span>}
            </dd>
          </div>
          <div className="flex justify-between items-center">
            <dt>Servicio corriendo</dt>
            <dd><StatusBadge ok={sysmon.serviceRunning} label={sysmon.serviceRunning ? 'Sí' : 'No'} /></dd>
          </div>
          <div className="flex justify-between items-center">
            <dt>Config MVP aplicada</dt>
            <dd><StatusBadge ok={sysmon.configApplied} label={sysmon.configApplied ? 'Sí' : 'No'} /></dd>
          </div>
          <div className="pt-1 text-xs text-gray-400">
            Eventos: ID 1 (ProcessCreate) · ID 3 (NetworkConnect) · ID 5 (ProcessTerminate) · ID 22 (DnsQuery)
          </div>
        </dl>

        <div className="px-4 py-3 bg-gray-50 border-t flex justify-end">
          <button
            disabled={!sysmon.executableFound || applying === 'sysmon'}
            onClick={() => handle(applySysmonConfig, 'sysmon')}
            className="text-sm px-4 py-1.5 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-300 disabled:cursor-not-allowed text-white rounded font-medium transition-colors"
          >
            {applying === 'sysmon' ? 'Aplicando…' : 'Aplicar configuración'}
          </button>
        </div>
      </div>

      {/* ─── Tarjeta Audit Policy ─── */}
      <div className="bg-white border rounded-lg overflow-hidden">
        <div className="px-4 py-3 bg-gray-50 border-b flex items-center gap-2">
          <span className="font-semibold text-sm text-gray-800">Política de Auditoría (Security Log)</span>
          <StatusBadge ok={(auditPolicy.processCreationEnabled && (auditPolicy.logonAuditEnabled ?? false))}
            label={(auditPolicy.processCreationEnabled && (auditPolicy.logonAuditEnabled ?? false)) ? 'Completa' : 'Incompleta'} />
        </div>

        <dl className="px-4 py-3 space-y-2 text-sm text-gray-600">
          <div className="flex justify-between items-center">
            <dt>Process Creation (eventos 4688 / 4689)</dt>
            <dd><StatusBadge ok={auditPolicy.processCreationEnabled}
              label={auditPolicy.processCreationEnabled ? 'Activa' : 'Inactiva'} /></dd>
          </div>
          <div className="flex justify-between items-center">
            <dt>Logon / Logoff (eventos 4624 / 4625)</dt>
            <dd><StatusBadge ok={auditPolicy.logonAuditEnabled ?? false}
              label={(auditPolicy.logonAuditEnabled ?? false) ? 'Activa' : 'Inactiva'} /></dd>
          </div>
          <div className="pt-1 text-xs text-gray-400">
            El Security Log es complementario a Sysmon. Sysmon aporta más datos (hash, línea de comandos con elevación).
          </div>
        </dl>

        <div className="px-4 py-3 bg-gray-50 border-t flex flex-wrap gap-2 justify-end">
          <button
            disabled={auditPolicy.processCreationEnabled || applying === 'audit'}
            onClick={() => handle(enableAuditPolicy, 'audit')}
            className="text-sm px-4 py-1.5 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-300 disabled:cursor-not-allowed text-white rounded font-medium transition-colors"
          >
            {applying === 'audit'
              ? 'Habilitando…'
              : auditPolicy.processCreationEnabled
                ? 'Process Creation ✓'
                : 'Habilitar Process Creation'}
          </button>
          <button
            disabled={(auditPolicy.logonAuditEnabled ?? false) || applying === 'audit-logon'}
            onClick={() => handle(enableLogonAudit, 'audit-logon')}
            className="text-sm px-4 py-1.5 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-300 disabled:cursor-not-allowed text-white rounded font-medium transition-colors"
          >
            {applying === 'audit-logon'
              ? 'Habilitando…'
              : (auditPolicy.logonAuditEnabled ?? false)
                ? 'Logon Audit ✓'
                : 'Habilitar Logon Audit'}
          </button>
        </div>
      </div>

      <button
        onClick={load}
        className="text-xs text-gray-400 hover:text-gray-600 transition-colors"
      >
        ↻ Refrescar estado
      </button>

      {/* ─── Tarjeta Almacenamiento ─── */}
      <div className="bg-white border rounded-lg overflow-hidden">
        <div className="px-4 py-3 bg-gray-50 border-b flex items-center gap-2">
          <span className="font-semibold text-sm text-gray-800">Almacenamiento (SQLite)</span>
          <span className="text-xs text-gray-500 bg-gray-200 px-2 py-0.5 rounded-full">
            {storage.fileSizeMb} MB
          </span>
        </div>
        <dl className="px-4 py-3 space-y-2 text-sm text-gray-600">
          <div className="flex justify-between">
            <dt>Tamaño BD</dt>
            <dd className="font-mono text-xs">{storage.fileSizeMb} MB ({storage.fileSizeBytes.toLocaleString()} bytes)</dd>
          </div>
          <div className="flex justify-between">
            <dt>Retención configurada</dt>
            <dd>{storage.retentionDays === 0 ? <span className="text-yellow-600 font-medium">Infinita</span> : `${storage.retentionDays} días`}</dd>
          </div>
          <div className="flex justify-between"><dt>Eventos de proceso</dt><dd className="font-mono">{storage.totalProcessEvents.toLocaleString()}</dd></div>
          <div className="flex justify-between"><dt>Eventos de red</dt><dd className="font-mono">{storage.totalNetworkEvents.toLocaleString()}</dd></div>
          <div className="flex justify-between"><dt>Eventos DNS</dt><dd className="font-mono">{storage.totalDnsEvents.toLocaleString()}</dd></div>
          <div className="flex justify-between"><dt>Eventos de acceso (logons)</dt><dd className="font-mono">{(storage.totalLogonEvents ?? 0).toLocaleString()}</dd></div>
          <div className="pt-1 text-xs text-gray-400">
            La purga automática se ejecuta cada hora. Cambia <code>RetentionDays</code> en appsettings.json (0 = sin límite).
          </div>
        </dl>
      </div>

      {/* ─── Tarjeta DNS de confianza ─── */}
      <div className="bg-white border rounded-lg overflow-hidden">
        <div className="px-4 py-3 bg-gray-50 border-b flex items-center gap-2">
          <span className="font-semibold text-sm text-gray-800">DNS de confianza</span>
          <span className="text-xs text-gray-500 bg-gray-200 px-2 py-0.5 rounded-full">
            {trustedDomains.length} dominios
          </span>
        </div>
        <div className="px-4 py-3 space-y-3">
          <p className="text-xs text-gray-500">
            Los dominios marcados aquí se excluyen al activar "Solo sospechosos" en la pestaña DNS.
            Las subentradas también se excluyen (ej. añadir <code>google.com</code> oculta también <code>mail.google.com</code>).
          </p>
          {/* Añadir dominio */}
          <div className="flex gap-2">
            <input
              className="flex-1 border rounded px-2 py-1 text-sm"
              placeholder="ejemplo.com"
              value={newDomain}
              onChange={e => setNewDomain(e.target.value)}
              onKeyDown={async e => {
                if (e.key === 'Enter' && newDomain.trim()) {
                  setDomainBusy(true);
                  const d = await addTrustedDomain(newDomain.trim());
                  setTrustedDomains(d);
                  setNewDomain('');
                  setDomainBusy(false);
                }
              }}
            />
            <button
              disabled={!newDomain.trim() || domainBusy}
              onClick={async () => {
                if (!newDomain.trim()) return;
                setDomainBusy(true);
                const d = await addTrustedDomain(newDomain.trim());
                setTrustedDomains(d);
                setNewDomain('');
                setDomainBusy(false);
              }}
              className="text-sm px-3 py-1 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:bg-gray-300 disabled:cursor-not-allowed"
            >
              + Añadir
            </button>
          </div>
          {/* Lista */}
          <ul className="divide-y max-h-64 overflow-y-auto rounded border">
            {trustedDomains.length === 0 && (
              <li className="px-3 py-2 text-xs text-gray-400 text-center">Sin dominios configurados</li>
            )}
            {trustedDomains.map(domain => (
              <li key={domain} className="flex items-center justify-between px-3 py-1.5 hover:bg-gray-50">
                <span className="text-sm font-mono text-gray-700">{domain}</span>
                <button
                  disabled={domainBusy}
                  onClick={async () => {
                    setDomainBusy(true);
                    const d = await removeTrustedDomain(domain);
                    setTrustedDomains(d);
                    setDomainBusy(false);
                  }}
                  className="text-xs text-red-500 hover:text-red-700 disabled:opacity-40 px-1"
                >
                  ✕
                </button>
              </li>
            ))}
          </ul>
        </div>
      </div>

      {/* Reglas de detección configurables */}
      <AlertRulesSection />
    </div>
  );
}

// ─── Sección de reglas de detección ──────────────────────────────────────────

const SEVERITY_COLORS: Record<string, string> = {
  High:   'bg-red-100 text-red-700',
  Medium: 'bg-yellow-100 text-yellow-700',
  Low:    'bg-blue-100 text-blue-700',
};

function AlertRulesSection() {
  const [rules, setRules]     = useState<AlertRuleConfig[]>([]);
  const [busy, setBusy]       = useState<number | null>(null);
  const [error, setError]     = useState('');

  useEffect(() => {
    getAlertRules().then(setRules).catch(() => setError('No se pudieron cargar las reglas.'));
  }, []);

  const toggle = async (rule: AlertRuleConfig) => {
    setBusy(rule.id);
    try {
      const updated = await patchAlertRule(rule.id, !rule.enabled);
      setRules(prev => prev.map(r => r.id === updated.id ? updated : r));
    } catch {
      setError('Error al guardar la regla.');
    } finally {
      setBusy(null);
    }
  };

  const changeSeverity = async (rule: AlertRuleConfig, sev: string) => {
    setBusy(rule.id);
    try {
      const updated = await patchAlertRule(rule.id, rule.enabled, sev);
      setRules(prev => prev.map(r => r.id === updated.id ? updated : r));
    } catch {
      setError('Error al guardar la severidad.');
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="bg-white rounded-xl border shadow-sm p-5">
      <h3 className="text-sm font-semibold text-gray-700 mb-1">Reglas de Detección</h3>
      <p className="text-xs text-gray-400 mb-4">Activa/desactiva reglas y ajusta la severidad de cada alerta</p>
      {error && <p className="text-xs text-red-600 mb-3">{error}</p>}
      <div className="divide-y">
        {rules.map(rule => (
          <div key={rule.id} className="flex items-center gap-3 py-2.5">
            {/* Toggle */}
            <button
              disabled={busy === rule.id}
              onClick={() => toggle(rule)}
              className={`relative inline-flex w-9 h-5 rounded-full transition-colors duration-200 focus:outline-none shrink-0 ${rule.enabled ? 'bg-blue-600' : 'bg-gray-300'} ${busy === rule.id ? 'opacity-50' : ''}`}
              title={rule.enabled ? 'Desactivar' : 'Activar'}
            >
              <span
                className={`absolute top-0.5 left-0.5 w-4 h-4 bg-white rounded-full shadow transition-transform duration-200 ${rule.enabled ? 'translate-x-4' : ''}`}
              />
            </button>

            {/* Nombre + descripción */}
            <div className="flex-1 min-w-0">
              <p className={`text-sm font-medium ${rule.enabled ? 'text-gray-800' : 'text-gray-400'}`}>
                {rule.name}
              </p>
              <p className="text-xs text-gray-400 truncate">{rule.description}</p>
            </div>

            {/* Selector de severidad */}
            <select
              disabled={busy === rule.id || !rule.enabled}
              value={rule.severity}
              onChange={e => changeSeverity(rule, e.target.value)}
              className={`text-xs rounded px-1.5 py-0.5 border-0 font-medium cursor-pointer focus:ring-1 focus:ring-blue-400 disabled:opacity-40 ${SEVERITY_COLORS[rule.severity] ?? ''}`}
            >
              <option value="High">High</option>
              <option value="Medium">Medium</option>
              <option value="Low">Low</option>
            </select>
          </div>
        ))}
      </div>
    </div>
  );
}
