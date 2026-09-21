import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { RunEvent } from '../api/models/run-event';
import { RunEventEnvelope } from '../api/models/run-event-envelope';
import { RunKind } from '../api/models/run-kind';
import { RunSubscription } from '../api/models/run-subscription';

/**
 * The realtime contract, generated like every other DTO.
 *
 * It used to be written out here by hand, because Swagger describes HTTP and a hub is not HTTP — with a
 * comment admitting that `RunEventType` was a copy of an enum the client switches on, and that adding a value
 * to it on the server changed nothing here until somebody remembered. `HubContractDocumentFilter` puts these
 * types into the OpenAPI document instead, so a change on that side is a compile error on this one.
 *
 * Re-exported rather than imported at each use site: everything that watches a run already imports this
 * service, and the types travel with it.
 */
export type { RunKind } from '../api/models/run-kind';
export type { RunEventType } from '../api/models/run-event-type';
export type { RunEvent } from '../api/models/run-event';
export type { RunEventEnvelope } from '../api/models/run-event-envelope';
export type { RunSubscription } from '../api/models/run-subscription';

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
    entry.lastSeq = Math.max(entry.lastSeq, subscription.lastSeq ?? 0);

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
    // Every field of the generated envelope is optional, because Swashbuckle reads a positional record's
    // properties and its nullability lives on the constructor. The hub always fills them; this is what
    // satisfies the compiler without pretending otherwise.
    if (!envelope.runId || envelope.seq === undefined || !envelope.event) {
      return;
    }

    const entry = this.watched.get(envelope.runId);

    if (!entry || envelope.seq <= entry.lastSeq) {
      // Either a run this page is not watching, or the overlap a replay always produces.
      return;
    }

    entry.lastSeq = envelope.seq;
    entry.events.next(envelope.event);
  }

  /**
   * Closes the connection and forgets what it was watching. What signing out calls.
   *
   * A hub's identity is decided during the handshake and never re-read, so a connection opened while signed in
   * goes on being that person's until it closes — the session is revoked, `/api/sites` answers 401, and the
   * socket keeps working. On a shared machine that is somebody else's turn started on your site, with your
   * budget. Demonstrated by connecting, logging out over HTTP, and invoking `StartChat` on the still-open
   * connection.
   *
   * The runs themselves are untouched, exactly as when a tab is closed: a turn outlives the page watching it,
   * and the person who signs back in re-attaches through `activeRunId`.
   *
   * The server checks the same thing per invocation (see `RealtimeHub.CallerId`), because this half depends on
   * the client choosing to call it.
   */
  async disconnect(): Promise<void> {
    for (const [, entry] of this.watched) entry.events.complete();

    this.watched.clear();

    const connection = this.connection;
    this.connection = undefined;
    this.starting = undefined;
    this.connected.set(false);

    // Stop rather than abort, so the server sees a clean close and the subscriber counts the orphan reaper
    // depends on come down now rather than on a timeout.
    if (connection) await connection.stop().catch(() => undefined);
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

        entry.lastSeq = Math.max(entry.lastSeq, subscription.lastSeq ?? 0);
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
