export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'disconnecting' | 'reconnecting' | 'error';

export interface Server {
  id: string;
  name: string;
  group: string;
  protocol: string;
  hostname: string;
  port: number;
  latency: number | null;
  managedBySubscription: boolean;
  isFavorite: boolean;
  countryCode: string | null;
  isAutomatic?: boolean;
  latencyMethod?: 'http' | 'tcp' | 'icmp' | null;
}

export interface ServerCatalog {
  servers: Server[];
  selectedServerId: string | null;
  importedCount: number;
}

export interface ServerFavoriteUpdate {
  id: string;
  isFavorite: boolean;
}

export interface ServerEditor {
  id: string;
  name: string;
  group: string;
  hostname: string;
  port: number;
  protocol: ServerProtocol;
  managedBySubscription: boolean;
  values: Record<string, string | null>;
  secretConfigured: Record<string, boolean>;
}

export type ServerProtocol = 'SS' | 'SSR' | 'SOCKS' | 'Trojan' | 'VMess' | 'VLESS' | 'SSH' | 'WireGuard';

export interface ServerEditorDocument {
  servers: ServerEditor[];
  protocols: ServerProtocol[];
  options: Record<string, string[]>;
}

export interface ServerDetailsWrite {
  id?: string;
  protocol: ServerProtocol;
  name: string;
  group: string;
  hostname: string;
  port: number;
  values: Record<string, string | null>;
  secrets: Record<string, string | null>;
  clearSecrets: string[];
}

export interface Mode {
  id: string;
  name: string;
  kind: string;
  supportsProcessSelection: boolean;
  selectionRole?: 'whole-computer' | 'selected-applications' | null;
}

export type EditableModeKind = 'ProcessMode' | 'TunMode';
export type InheritedSwitch = boolean | null;

export interface ModeDetails {
  id: string;
  name: string;
  kind: 'ProcessMode' | 'TunMode' | 'ShareMode';
  source: 'built-in' | 'custom';
  editable: boolean;
  fileName: string;
  filterIcmp: InheritedSwitch;
  filterTcp: InheritedSwitch;
  filterUdp: InheritedSwitch;
  filterDns: InheritedSwitch;
  includeChildProcesses: InheritedSwitch;
  icmpDelayMs: number | null;
  proxyDns: InheritedSwitch;
  handleOnlyDns: InheritedSwitch;
  dnsHost: string | null;
  filterLoopback: boolean;
  filterIntranet: boolean;
  bypassRules: string[];
  handleRules: string[];
  shareArgument: string | null;
}

export interface ModeWrite {
  id?: string;
  name: string;
  kind: EditableModeKind;
  filterIcmp: InheritedSwitch;
  filterTcp: InheritedSwitch;
  filterUdp: InheritedSwitch;
  filterDns: InheritedSwitch;
  includeChildProcesses: InheritedSwitch;
  icmpDelayMs: number | null;
  proxyDns: InheritedSwitch;
  handleOnlyDns: InheritedSwitch;
  dnsHost: string | null;
  filterLoopback: boolean;
  filterIntranet: boolean;
  bypassRules: string[];
  handleRules: string[];
}

export interface ModeCatalog {
  modes: Mode[];
  details: ModeDetails[];
  selectedModeId: string | null;
}

export interface QuickProfile {
  slot: number;
  status: 'empty' | 'ready' | 'missing';
  name: string | null;
  serverName: string | null;
  modeName: string | null;
  serverId: string | null;
  modeId: string | null;
}

export interface ProfileActivation {
  serverId: string;
  modeId: string;
}

export interface Subscription {
  id: string;
  remark: string;
  url: string;
  userAgent: string;
  enabled: boolean;
  serverCount: number;
  refreshStatus: 'idle' | 'refreshing' | 'success' | 'error';
  lastUpdatedAt: string | null;
  lastError: string | null;
}

export type SubscriptionInput = Pick<Subscription, 'remark' | 'url' | 'userAgent' | 'enabled'> & { id?: string };

export interface ProcessInfo {
  processIds: number[];
  name: string;
  executable: string;
  path: string | null;
  iconDataUrl: string | null;
}

export interface LogEntry {
  id: number;
  timestamp: string;
  level: 'DEBUG' | 'INFO' | 'WARNING' | 'ERROR';
  message: string;
}

export interface ConnectionState {
  status: ConnectionStatus;
  message: string;
  serverId: string | null;
  modeId: string | null;
  connectedAt: string | null;
}

export interface TrafficSnapshot {
  receivedBytes: number;
  sentBytes: number;
  received: string;
  sent: string;
  durationSeconds: number;
  connectedAt: string | null;
}

export interface SubscriptionRefreshSummary {
  serverCount: number;
  updatedSubscriptions: number;
  failedSubscriptions: number;
}

export interface SettingsDocument {
  settings: SettingsData;
  languages: string[];
  canUpdate: boolean;
}

export interface SettingsData {
  general: GeneralSettings;
  connection: ConnectionSettings;
  routing: RoutingSettings;
  subscriptions: SubscriptionSettings;
  dns: DnsSettings;
  startup: StartupSettings;
  updates: UpdateSettings;
  advanced: AdvancedSettings;
}

export interface GeneralSettings { language: string; profileCount: number; profileColumns: number; }
export interface ConnectionSettings {
  socks5Port: number; httpPort: number; localAddress: string; pingMethod: 'http' | 'tcp' | 'icmp';
  latencyTestUrl?: string;
  requestTimeoutMs: number; detectionIntervalSeconds: number; startupPingDelaySeconds: number;
  stunHost: string; stunPort: number;
}
export interface RoutingSettings {
  filterTcp: boolean; filterUdp: boolean; filterIcmp: boolean; icmpDelayMs: number;
  filterDns: boolean; includeChildProcesses: boolean; proxyDns: boolean; handleOnlyDns: boolean; dnsHost: string;
}
export interface SubscriptionSettings { updateOnLaunch: boolean; }
export interface DnsSettings {
  chinaDns: string; otherDns: string; tunAddress: string; tunNetmask: string; tunGateway: string;
  useCustomDns: boolean; tunDns: string; proxyTunDns: boolean; bypassIps: string[];
}
export interface StartupSettings {
  runAtStartup: boolean; connectOnLaunch: boolean; minimizeOnLaunch: boolean;
  closeToTray: boolean; stopConnectionOnExit: boolean;
}
export interface UpdateSettings { checkOnLaunch: boolean; includeBetaVersions: boolean; }
export interface AdvancedSettings {
  xrayCone: boolean; allowInsecureTls: boolean; useMux: boolean; tcpFastOpen: boolean;
  hideUnsupportedEnvironmentWarning: boolean; kcpMtu: number; kcpTti: number;
  kcpUplinkCapacity: number; kcpDownlinkCapacity: number; kcpReadBufferSize: number;
  kcpWriteBufferSize: number; kcpCongestion: boolean;
}

export interface Routing {
  mode: 'all' | 'selected';
  processes: string[];
}

export interface Bootstrap {
  servers: Server[];
  modes: Mode[];
  profiles: QuickProfile[];
  subscriptions: Subscription[];
  connection: ConnectionState;
  routing: Routing;
  logs: LogEntry[];
  selectedServerId: string | null;
  selectedModeId: string | null;
  closeToTray: boolean;
  language?: string;
}

export interface RpcError {
  code: string;
  message: string;
  details?: string;
}

export interface RpcResponse<T = unknown> {
  id: string;
  result?: T;
  error?: RpcError;
}

export interface AppEvent<T = unknown> {
  type: 'event';
  event: string;
  payload: T;
}

export interface DiagnosticComponent {
  id: string;
  name: string;
  status: 'ready' | 'missing' | 'missing-source' | 'outdated' | 'error';
  summary: string;
  version: string | null;
  canRepair: boolean;
}

export interface DiagnosticsSnapshot {
  appVersion: string;
  runtimeVersion: string;
  operatingSystem: string;
  architecture: string;
  isAdministrator: boolean;
  webView2Version: string | null;
  checkedAt: string;
  components: DiagnosticComponent[];
}

export type UpdateStatus = 'idle' | 'checking' | 'up-to-date' | 'available' | 'downloading' | 'ready' | 'applying' | 'applied' | 'error';

export interface UpdateState {
  status: UpdateStatus;
  currentVersion: string;
  latestVersion: string | null;
  releaseNotes: string | null;
  releaseUrl: string | null;
  progress: number;
  error: string | null;
  canDownload: boolean;
  canApply: boolean;
}
