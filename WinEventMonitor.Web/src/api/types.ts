export interface ProcessEvent {
  id: number;
  timestamp: string;
  eventType: 'Create' | 'Terminate';
  eventSource: 'Security' | 'Sysmon';
  pid: number;
  parentPid?: number;
  processName: string;
  commandLine?: string;
  userName?: string;
  isElevated: boolean;
  integrityLevel?: string;
  sha256?: string;
}

export interface NetworkEvent {
  id: number;
  timestamp: string;
  pid: number;
  processName: string;
  executablePath?: string | null;
  userName?: string;
  protocol?: string;
  sourceIp?: string;
  sourcePort?: number;
  destinationIp?: string;
  destinationPort?: number;
  initiated: boolean;
}

export interface DnsEvent {
  id: number;
  timestamp: string;
  pid: number;
  processName: string;
  userName?: string;
  queryName: string;
  queryResults?: string;
  queryStatus?: string;
}

export interface PagedResult<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ProcessFilters {
  from?: string;
  to?: string;
  name?: string;
  user?: string;
  elevated?: boolean;
  page: number;
  pageSize: number;
}

export interface NetworkFilters {
  from?: string;
  to?: string;
  process?: string;
  destIp?: string;
  destPort?: number;
  page: number;
  pageSize: number;
}

export interface DnsFilters {
  from?: string;
  to?: string;
  process?: string;
  domain?: string;
  excludeTrusted?: boolean;
  page: number;
  pageSize: number;
}

export interface SysmonStatus {
  executableFound: boolean;
  executablePath: string | null;
  serviceRunning: boolean;
  configApplied: boolean;
}

export interface AuditPolicyStatus {
  processCreationEnabled: boolean;
  logonAuditEnabled?: boolean;
}

export interface StorageInfo {
  fileSizeBytes: number;
  fileSizeMb: string;
  retentionDays: number;
  totalProcessEvents: number;
  totalNetworkEvents: number;
  totalDnsEvents: number;
  totalLogonEvents?: number;
}

export interface SetupStatus {
  sysmon: SysmonStatus;
  auditPolicy: AuditPolicyStatus;
  storage: StorageInfo;
}

export interface LiveProcess {
  pid: number;
  parentPid: number;
  name: string;
  commandLine: string | null;
  executablePath: string | null;
  userName: string | null;
  isElevated: boolean;
  integrityLevel: string | null;
  sha256: string | null;
  source: 'live' | 'db';
  cpuPercent?: number | null;
  workingSetMb?: number | null;
}

export interface ProcessTreeNode extends LiveProcess {
  children: ProcessTreeNode[];
}

export interface AlertEvent {
  id: string;
  timestamp: string;
  severity: 'High' | 'Medium' | 'Low';
  rule: string;
  description: string;
  pid: number | null;
  processName: string | null;
  details: string | null;
  mitreTechnique: string | null;
}

export interface StatsCountItem { count: number; }
export interface StatsKeyCount { [key: string]: number; }

// ─── Sistema / Salud ──────────────────────────────────────────────────────────

export interface CpuInfo {
  totalPercent: number;
  coreCount: number;
}

export interface RamInfo {
  totalMb: number;
  usedMb: number;
  freeMb: number;
}

export interface DiskInfo {
  name: string;
  totalGb: number;
  freeGb: number;
}

export interface ProcessMetric {
  pid: number;
  name: string;
  cpuPercent: number;
  workingSetMb: number;
  ioReadBytesSec: number;
  ioWriteBytesSec: number;
}

export interface SystemSnapshot {
  cpu: CpuInfo;
  ram: RamInfo;
  disk: DiskInfo[];
  processes: ProcessMetric[];
  generatedAt: string;
}

export interface HistoryPoint {
  at: string;
  cpuPct: number;
  ramPct: number;
}

export interface AlertRuleConfig {
  id: number;
  name: string;
  severity: 'High' | 'Medium' | 'Low';
  enabled: boolean;
  description: string;
}

// ─── Logon / Acceso remoto ───────────────────────────────────────────────────

export interface LogonEvent {
  id: number;
  timestamp: string;
  eventId: number;
  success: boolean;
  logonType: number;
  logonTypeName: string;
  userName: string | null;
  domain: string | null;
  sourceIp: string | null;
  sourcePort: number | null;
  workstationName: string | null;
  logonProcessName: string | null;
  authPackage: string | null;
  failureReason: string | null;
}

export interface LogonFilters {
  from?: string;
  to?: string;
  user?: string;
  sourceIp?: string;
  type?: number;
  success?: boolean;
  remoteOnly?: boolean;
  page: number;
  pageSize: number;
}

export interface LogonSummary {
  failures24h: number;
  rdpSessions24h: number;
  networkLogons24h: number;
  uniqueSourceIps: number;
  topAttackers: Array<{ ip: string; count: number }>;
  recentRemote: LogonEvent[];
}

// ─── Conexiones TCP ────────────────────────────────────────────────────────

export interface TcpListenRow {
  protocol: string;
  localPort: number;
  localIp: string;
  state: string;
  pid: number;
  processName: string;
}

export interface TcpEstabRow {
  protocol: string;
  localPort: number;
  localIp: string;
  remoteIp: string;
  remotePort: number;
  state: string;
  pid: number;
  processName: string;
}

export interface ConnectionsSnapshot {
  listening: TcpListenRow[];
  established: TcpEstabRow[];
  generatedAt: string;
}

export interface ActivityHour {
  hour: number;
  processes: number;
  network: number;
}

export interface Stats {
  totals: { totalProcesses: number; totalNetwork: number; totalDns: number; totalAlerts: number };
  last24h: { proc24: number; net24: number; dns24: number; alerts24: number };
  alertsBySeverity: Array<{ severity: string; count: number }>;
  topIps: Array<{ ip: string; count: number }>;
  topNetProcs: Array<{ process: string; count: number }>;
  topDomains: Array<{ domain: string; count: number }>;
  activityByHour: ActivityHour[];
  recentAlerts: Array<{ id: string; timestamp: string; severity: string; rule: string; description: string; processName: string | null }>;
  generatedAt: string;
}

// ─── Sysmon avanzado (IDs 7, 8, 10) ─────────────────────────────────────────

export interface SysmonAdvancedEvent {
  id: number;
  timestamp: string;
  eventId: number;
  sourcePid: number;
  sourceProcessName: string;
  imagePath?: string | null;
  signed?: boolean | null;
  signature?: string | null;
  signatureStatus?: string | null;
  sha256?: string | null;
  targetPid?: number | null;
  targetProcessName?: string | null;
  startAddress?: string | null;
  startModule?: string | null;
  startFunction?: string | null;
  grantedAccess?: string | null;
  callTrace?: string | null;
}

export interface ProcessTimeline {
  pid: number;
  processName: string;
  processes: ProcessEvent[];
  network: NetworkEvent[];
  dns: DnsEvent[];
  alerts: AlertEvent[];
  advanced: SysmonAdvancedEvent[];
}

export interface VtResult {
  available: boolean;
  found: boolean;
  sha256: string;
  name?: string | null;
  malicious?: number;
  total?: number;
  verdict?: 'clean' | 'suspicious' | 'malicious';
  message?: string;
  error?: string;
}
