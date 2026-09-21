import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Subject } from 'rxjs';

/** Mirrors `RunKind` on the backend. Two kinds, one connection. */
export type RunKind = 'Chat' | 'Deploy';

/** Mirrors `RunEventType`. Kept as a union rather than imported from the generated client because the
 *  hub's payloads are not described by Swagger — see the note in docs/agent-plan.md about pinning them
 *  into the OpenAPI document, which is the change that lets this import them instead. */
export type RunEventType =
  | 'TextDelta'
  | 'MessageCompleted'
  | 'Activity'
  | 'FileChanged'
  | 'WorkspaceProgress'
  | 'VersionCommitted'
  | 'BuildFailed'
  | 'DeploymentProgress'
  | 'Completed'
  | 'Failed';

export interface RunEvent {
  type: RunEventType;
  text?: string | null;
  /** A phrase, a path or a status — whatever the event type says it is. */
  detail?: string | null;
  error?: string | null;
  versionNanoid?: string | null;
  createdAt: string;
}

export interface RunEventEnvelope {
  runKind: RunKind;
  runId: string;
  seq: number;
  event: RunEvent;
  isTerminal: boolean;
}

interface RunSubscription {
  runKind: RunKind;
  runId: string;
  lastSeq: number;
  isLive: boolean;
}

interface Watched {
  kind: RunKind;
  events: Subject<RunEvent>;
  lastSeq: number;
}

/**
 * One SignalR connection, multiplexing every run the page cares about.
 *
 * Three things here are load-bearing, and all three exist because a run deliberately outlives the
 * connection that started it:
 *
 * 1. **`lastSeq` per run** is both the resume point and the duplicate filter. The hub joins the group
 *    before it replays, so a reconnect sees a small overlap; discarding anything already seen is what
 *    makes that safe.
 * 2. **Resubscribe on reconnect.** SignalR's automatic reconnect gives a new connection with no group
 *    memberships, so every watched run is re-subscribed from its own `lastSeq`.
 * 3. **Browser only.** SSR must never open a socket: it has no cookies to authenticate with, and a
 *    connection attempt during prerendering does not fail, it hangs until the build times out.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private connection?: HubConnection;
  private starting?: Promise<void>;
  private readonly watched = new Map<string, Watched>();

  /**
   * Whether the hub connection is up. For the "reconnecting…" line in the editor — which is shown only while a
   * turn is in flight, because this is false on every editor nobody has spoken to yet: the connection is made
   * on the first `startChat` or `watch`, not on load, so that reading a site holds no socket.
   *
   * Not an error state either way. A run outlives the connection by design and keeps going regardless.
   */
  readonly connected = signal(false);

  /**
   * Starts a turn and returns its run id. Two steps — start, then `watch` — so that a page which
   * reloads mid-turn re-attaches by exactly the same path as one that started the turn itself.
   */
  async startChat(siteNanoid: string, message: string): Promise<string> {
    const hub = await this.ensureConnected();

    const started = await hub.invoke<{ runId: string; conversationNanoid?: string | null }>('StartChat', {
      siteNanoid,
      message,
    });

    return started.runId;
  }

  /**
   * Subscribes to a run and returns its event stream. Replays everything since `lastSeq`, so calling
   * this after a reload plays the turn back from wherever the page left off.
   */
  async watch(kind: RunKind, runId: string): Promise<Subject<RunEvent>> {
    const hub = await this.ensureConnected();
    const existing = this.watched.get(runId);

    if (existing) {
      return existing.events;
    }

    const entry: Watched = { kind, events: new Subject<RunEvent>(), lastSeq: 0 };
    this.watched.set(runId, entry);

    const subscription = await hub.invoke<RunSubscription>('Subscribe', kind, runId, entry.lastSeq);
    entry.lastSeq = Math.max(entry.lastSeq, subscription.lastSeq);

    return entry.events;
  }

  async unwatch(runId: string): Promise<void> {
    const entry = this.watched.get(runId);

    if (!entry) {
      return;
    }

    this.watched.delete(runId);
    entry.events.complete();

    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('Unsubscribe', entry.kind, runId);
    }
  }

  /** The only thing that stops a run. Closing the tab deliberately does not. */
  async cancel(runId: string): Promise<void> {
    const hub = await this.ensureConnected();
    await hub.invoke('Cancel', runId);
  }

  /** The live run for a conversation or deployment, for a page that has lost the run id to a reload. */
  async findRun(kind: RunKind, correlationId: string): Promise<string | null> {
    const hub = await this.ensureConnected();

    return hub.invoke<string | null>('FindRun', kind, correlationId);
  }

  private async ensureConnected(): Promise<HubConnection> {
    if (!this.isBrowser) {
      throw new Error('The realtime connection is browser-only.');
    }

    if (this.connection?.state === HubConnectionState.Connected) {
      return this.connection;
    }

    this.connection ??= this.build();
    this.starting ??= this.connection
      .start()
      .then(() => {
        this.connected.set(true);
      })
      .finally(() => (this.starting = undefined));

    await this.starting;

    return this.connection;
  }

  private build(): HubConnection {
    // Same origin, so the hub URL is a path and the session cookie travels with the handshake. The
    // browser cannot set an Authorization header on a WebSocket upgrade, which is why the backend reads
    // the cookie — see CookieAuthenticationMiddleware.
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/realtime')
      .withAutomaticReconnect()
      .build();

    connection.on('RunEvent', (envelope: RunEventEnvelope) => this.dispatch(envelope));

    connection.onreconnected(() => {
      this.connected.set(true);
      void this.resubscribeAll();
    });

    connection.onreconnecting(() => this.connected.set(false));
    connection.onclose(() => this.connected.set(false));

    return connection;
  }

  private dispatch(envelope: RunEventEnvelope): void {
    const entry = this.watched.get(envelope.runId);

    if (!entry || envelope.seq <= entry.lastSeq) {
      // Either a run this page is not watching, or the overlap a replay always produces.
      return;
    }

    entry.lastSeq = envelope.seq;
    entry.events.next(envelope.event);
  }

  /**
   * A new connection has no group memberships, so every watched run has to be re-subscribed — from its
   * own `lastSeq`, which is what makes the reconnect a resume rather than a restart.
   */
  private async resubscribeAll(): Promise<void> {
    for (const [runId, entry] of this.watched) {
      try {
        const subscription = await this.connection!.invoke<RunSubscription>(
          'Subscribe',
          entry.kind,
          runId,
          entry.lastSeq,
        );

        entry.lastSeq = Math.max(entry.lastSeq, subscription.lastSeq);
      } catch {
        // The run finished and was evicted while the connection was down. Its terminal event was in the
        // replay the reconnect just missed, so the page is left showing a turn that ended — which the
        // next reload corrects from the persisted thread.
        this.watched.delete(runId);
        entry.events.complete();
      }
    }
  }
}
