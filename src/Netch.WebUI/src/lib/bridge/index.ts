import type {
  AppEvent,
  Bootstrap,
  ConnectionState,
  DiagnosticsSnapshot,
  LogEntry,
  Mode,
  ModeCatalog,
  ModeWrite,
  ProfileActivation,
  ProcessInfo,
  QuickProfile,
  Routing,
  RpcError,
  RpcResponse,
  Server,
  ServerCatalog,
  ServerDetailsWrite,
  ServerEditorDocument,
  SettingsData,
  SettingsDocument,
  Subscription,
  SubscriptionInput,
  SubscriptionRefreshSummary,
  UpdateState
} from '../types';

type EventHandler = (payload: unknown) => void;
type PendingRequest = {
  resolve: (value: unknown) => void;
  reject: (reason: BridgeError) => void;
  timeout: ReturnType<typeof setTimeout>;
};

export class BridgeError extends Error {
  constructor(public readonly rpc: RpcError) {
    super(rpc.message);
    this.name = 'BridgeError';
  }
}

class DesktopBridge {
  private sequence = 0;
  private pending = new Map<string, PendingRequest>();
  private listeners = new Map<string, Set<EventHandler>>();
  private host = window.chrome?.webview;
  readonly demo = import.meta.env.DEV && !this.host;
  private mock = import.meta.env.DEV && !this.host ? import('./mock').then(({ createMockBridge }) => createMockBridge(
    (event, payload) => this.listeners.get(event)?.forEach(listener => listener(payload))
  )) : null;

  constructor() {
    this.host?.addEventListener('message', this.onMessage);
  }

  get available(): boolean {
    return Boolean(this.host);
  }

  invoke<T>(method: string, params: object = {}, timeoutMs = 30_000): Promise<T> {
    if (this.mock) return this.mock.then(mock => mock.invoke(method, JSON.parse(JSON.stringify(params)))) as Promise<T>;
    if (!this.host) {
      return Promise.reject(new BridgeError({
        code: 'BRIDGE_UNAVAILABLE',
        message: 'Netch desktop bridge is unavailable. Start the WebUI inside Netch.'
      }));
    }

    const id = `${Date.now().toString(36)}-${(++this.sequence).toString(36)}`;
    return new Promise<T>((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new BridgeError({ code: 'TIMEOUT', message: `Operation '${method}' timed out.` }));
      }, timeoutMs);

      this.pending.set(id, {
        resolve: resolve as (value: unknown) => void,
        reject,
        timeout
      });
      this.host!.postMessage({ id, method, params });
    });
  }

  on<T>(event: string, handler: (payload: T) => void): () => void {
    const handlers = this.listeners.get(event) ?? new Set<EventHandler>();
    handlers.add(handler as EventHandler);
    this.listeners.set(event, handlers);
    return () => handlers.delete(handler as EventHandler);
  }

  dispose() {
    this.listeners.clear();
    if (this.mock) void this.mock.then(mock => mock.dispose());
  }

  private onMessage = (event: MessageEvent<RpcResponse | AppEvent>) => {
    const message = event.data;
    if ('type' in message && message.type === 'event') {
      this.listeners.get(message.event)?.forEach((listener) => listener(message.payload));
      return;
    }

    const response = message as RpcResponse;
    const request = this.pending.get(response.id);
    if (!request) return;

    clearTimeout(request.timeout);
    this.pending.delete(response.id);
    if (response.error) request.reject(new BridgeError(response.error));
    else request.resolve(response.result);
  };
}

export const bridge = new DesktopBridge();
if (import.meta.hot) import.meta.hot.dispose(() => bridge.dispose());

export const netch = {
  bootstrap: () => bridge.invoke<Bootstrap>('app.bootstrap'),
  connection: {
    getState: () => bridge.invoke<ConnectionState>('connection.getState'),
    connect: (serverId: string, modeId: string) =>
      bridge.invoke<ConnectionState>('connection.connect', { serverId, modeId }),
    disconnect: () => bridge.invoke<ConnectionState>('connection.disconnect'),
    cancel: () => bridge.invoke<ConnectionState>('connection.cancel'),
    reconnect: () => bridge.invoke<ConnectionState>('connection.reconnect')
  },
  servers: {
    list: () => bridge.invoke<Server[]>('servers.list'),
    details: () => bridge.invoke<ServerEditorDocument>('servers.details'),
    saveDetails: (value: ServerDetailsWrite) => bridge.invoke<ServerCatalog>('servers.saveDetails', value),
    select: (id: string) => bridge.invoke<Server[]>('servers.select', { id }),
    ping: (serverIds?: string[]) => bridge.invoke<Server[]>('servers.ping', { serverIds: serverIds ?? null }, 900_000),
    import: (text: string, group?: string) => bridge.invoke<ServerCatalog>('servers.import', { text, group: group ?? null }),
    delete: (id: string) => bridge.invoke<ServerCatalog>('servers.delete', { id }),
    setFavorite: (id: string, isFavorite: boolean) =>
      bridge.invoke<ServerCatalog>('servers.setFavorite', { id, isFavorite })
  },
  modes: {
    list: () => bridge.invoke<Mode[]>('modes.list'),
    details: () => bridge.invoke<ModeCatalog>('modes.details'),
    select: (id: string) => bridge.invoke<Mode[]>('modes.select', { id }),
    save: (value: ModeWrite) => bridge.invoke<ModeCatalog>('modes.save', value),
    delete: (id: string) => bridge.invoke<ModeCatalog>('modes.delete', { id })
  },
  profiles: {
    list: () => bridge.invoke<QuickProfile[]>('profiles.list'),
    save: (slot: number, name: string, serverId: string, modeId: string) =>
      bridge.invoke<QuickProfile[]>('profiles.save', { slot, name, serverId, modeId }),
    delete: (slot: number) => bridge.invoke<QuickProfile[]>('profiles.delete', { slot }),
    activate: (slot: number) => bridge.invoke<ProfileActivation>('profiles.activate', { slot })
  },
  subscriptions: {
    list: () => bridge.invoke<Subscription[]>('subscriptions.list'),
    save: (value: SubscriptionInput) =>
      bridge.invoke<Subscription[]>('subscriptions.save', value),
    delete: (id: string) => bridge.invoke<Subscription[]>('subscriptions.delete', { id }),
    refresh: (id: string) => bridge.invoke<number>('subscriptions.refresh', { id }),
    refreshAll: () => bridge.invoke<SubscriptionRefreshSummary>('subscriptions.refreshAll')
  },
  processes: {
    list: () => bridge.invoke<ProcessInfo[]>('processes.list'),
    pickExecutable: () => bridge.invoke<ProcessInfo | null>('processes.pickExecutable')
  },
  routing: {
    get: () => bridge.invoke<Routing>('routing.get'),
    setProcesses: (processes: string[]) =>
      bridge.invoke<{ routing: Routing; modeId: string }>('routing.setProcesses', { processes })
  },
  logs: {
    get: () => bridge.invoke<LogEntry[]>('logs.get'),
    clear: () => bridge.invoke<boolean>('logs.clear')
  },
  settings: {
    get: () => bridge.invoke<SettingsDocument>('settings.get'),
    update: (settings: SettingsData) => bridge.invoke<SettingsDocument>('settings.update', { settings })
  },
  diagnostics: {
    get: () => bridge.invoke<DiagnosticsSnapshot>('diagnostics.get'),
    installDriver: (driverId: string) => bridge.invoke<DiagnosticsSnapshot>('drivers.install', { driverId }, 120_000)
  },
  updates: {
    getState: () => bridge.invoke<UpdateState>('updates.getState'),
    check: (includePrerelease: boolean) => bridge.invoke<UpdateState>('updates.check', { includePrerelease }, 120_000),
    download: () => bridge.invoke<UpdateState>('updates.download', {}, 900_000),
    apply: () => bridge.invoke<UpdateState>('updates.apply', {}, 900_000)
  },
  window: {
    beginDrag: () => bridge.invoke<boolean>('window.beginDrag'),
    minimize: () => bridge.invoke<boolean>('window.minimize'),
    toggleMaximize: () => bridge.invoke<boolean>('window.toggleMaximize'),
    close: () => bridge.invoke<boolean>('window.close')
  }
};
