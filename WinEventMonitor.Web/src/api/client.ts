import axios from 'axios';
import type {
  PagedResult,
  ProcessEvent,
  NetworkEvent,
  DnsEvent,
  ProcessFilters,
  NetworkFilters,
  DnsFilters,
  SetupStatus,
  LiveProcess,
  AlertEvent,
  Stats,
  SystemSnapshot,
  HistoryPoint,
  LogonEvent,
  LogonFilters,
  LogonSummary,
  ConnectionsSnapshot,
  AlertRuleConfig,
  ProcessTimeline,
  VtResult,
} from './types';

// La API Key se lee del fichero generado por el servicio.
// En dev la pasamos por variable de entorno del navegador.
// En producción (WebView2) usamos 127.0.0.1 para evitar que Chromium
// resuelva "localhost" a ::1 (IPv6) en vez de 127.0.0.1 (IPv4).
const API_KEY = import.meta.env.VITE_API_KEY ?? '';
const BASE_URL = import.meta.env.VITE_API_URL ?? 'http://127.0.0.1:51847';

const client = axios.create({
  baseURL: BASE_URL,
  headers: { 'X-Api-Key': API_KEY },
});

function toParams(obj: object) {
  return Object.fromEntries(
    Object.entries(obj).filter(([, v]) => v !== undefined && v !== '')
  );
}

export async function getProcesses(filters: ProcessFilters): Promise<PagedResult<ProcessEvent>> {
  const { data } = await client.get('/api/processes', { params: toParams(filters) });
  return data;
}

export async function getNetwork(filters: NetworkFilters): Promise<PagedResult<NetworkEvent>> {
  const { data } = await client.get('/api/network', { params: toParams(filters) });
  return data;
}

export async function getDns(filters: DnsFilters): Promise<PagedResult<DnsEvent>> {
  const { data } = await client.get('/api/dns', { params: toParams(filters) });
  return data;
}

export async function getSetupStatus(): Promise<SetupStatus> {
  const { data } = await client.get('/api/setup/status');
  return data;
}

export async function applySysmonConfig(): Promise<{ message: string }> {
  const { data } = await client.post('/api/setup/sysmon');
  return data;
}

export async function enableAuditPolicy(): Promise<{ message: string }> {
  const { data } = await client.post('/api/setup/audit');
  return data;
}

export async function enableLogonAudit(): Promise<{ message: string }> {
  const { data } = await client.post('/api/setup/audit-logon');
  return data;
}

export async function getLiveProcesses(): Promise<LiveProcess[]> {
  const { data } = await client.get('/api/processes/live');
  return data;
}

export async function getProcessTree(hours: number): Promise<LiveProcess[]> {
  const { data } = await client.get('/api/processes/tree', { params: { hours } });
  return data;
}

// ─── Dominios DNS de confianza ─────────────────────────────────────────

export async function getTrustedDomains(): Promise<string[]> {
  const { data } = await client.get('/api/setup/trusted-domains');
  return data;
}

export async function addTrustedDomain(domain: string): Promise<string[]> {
  const { data } = await client.post('/api/setup/trusted-domains/add', { domain });
  return data;
}

export async function removeTrustedDomain(domain: string): Promise<string[]> {
  const { data } = await client.post('/api/setup/trusted-domains/remove', { domain });
  return data;
}

// ─── Alertas ──────────────────────────────────────────────────────

export async function getAlerts(
  page = 1,
  pageSize = 50
): Promise<{ data: AlertEvent[]; total: number; page: number; pageSize: number }> {
  const { data } = await client.get('/api/alerts', { params: { page, pageSize } });
  return data;
}

export async function getAlertCount(): Promise<number> {
  const { data } = await client.get('/api/alerts/count');
  return data.count as number;
}

export async function clearAlerts(): Promise<void> {
  await client.delete('/api/alerts');
}

// ─── Estadísticas / Dashboard ─────────────────────────────────────

export async function getStats(): Promise<Stats> {
  const { data } = await client.get('/api/stats');
  return data;
}

// ─── Sistema / Salud ──────────────────────────────────────────────

export async function getSystemHealth(): Promise<SystemSnapshot> {
  const { data } = await client.get('/api/system/health');
  return data;
}

export async function getSystemHistory(): Promise<HistoryPoint[]> {
  const { data } = await client.get('/api/system/history');
  return data;
}

// ─── Acceso remoto / Logons ────────────────────────────────────────

export async function getLogons(
  filters: LogonFilters
): Promise<{ data: LogonEvent[]; total: number; page: number; pageSize: number }> {
  const { data } = await client.get('/api/logons', { params: toParams(filters) });
  return data;
}

export async function getLogonSummary(): Promise<LogonSummary> {
  const { data } = await client.get('/api/logons/summary');
  return data;
}

// ─── Conexiones TCP activas ────────────────────────────────────────

export async function getConnections(): Promise<ConnectionsSnapshot> {
  const { data } = await client.get('/api/system/connections');
  return data;
}

// ─── Reglas de alerta configurables ───────────────────────────────

export async function getAlertRules(): Promise<AlertRuleConfig[]> {
  const { data } = await client.get('/api/alert-rules');
  return data;
}

export async function patchAlertRule(
  id: number,
  enabled: boolean,
  severity?: string
): Promise<AlertRuleConfig> {
  const { data } = await client.patch(`/api/alert-rules/${id}`, { enabled, severity });
  return data;
}

// ─── Timeline + VirusTotal ────────────────────────────────────────────────────

export async function getAlertsByPid(): Promise<Record<number, number>> {
  const { data } = await client.get('/api/alerts/pids');
  return data;
}

export async function getProcessTimeline(pid: number): Promise<ProcessTimeline> {
  const { data } = await client.get(`/api/processes/${pid}/timeline`);
  return data;
}

export async function getVtReport(sha256: string): Promise<VtResult> {
  const { data } = await client.get(`/api/virustotal/${sha256}`);
  return data;
}
