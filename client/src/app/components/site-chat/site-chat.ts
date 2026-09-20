import { Component, ElementRef, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Icon } from '../../shared/icon';
import { ChatService } from '../../services/chat.service';
import { RealtimeService, RunEvent } from '../../services/realtime.service';
import { messageOf } from '../../models/problem-details';
import { ChatMessageResponse } from '../../api/models/chat-message-response';

/** A line in the transcript. One shape for everything the stream can produce. */
export interface ChatEntry {
  kind: 'user' | 'assistant' | 'notice' | 'activity' | 'files' | 'waking' | 'version' | 'build' | 'error';
  text: string;
  /** `files` entries collect the paths the turn has written so far, rather than one chip per write. */
  paths?: string[];
  versionNanoid?: string;
}

/**
 * How a stored message's role is drawn. `System` is the one worth naming: it is what the *app* said about a
 * turn — "Stopped. Nothing was changed." — and drawing it as an assistant bubble would put words in the
 * agent's mouth. `Tool` is mapped for completeness; nothing persists tool traffic today.
 */
const KIND_OF_ROLE: Record<ChatMessageResponse['role'], ChatEntry['kind']> = {
  User: 'user',
  Assistant: 'assistant',
  System: 'notice',
  Tool: 'activity',
};

/**
 * The chat. This is the product's main surface, and most of its complexity is in one place: what to do
 * when the page and the run disagree about what has happened.
 *
 * The rules it follows, all of which come from the run outliving the connection:
 *
 * - A turn is started, then watched, as two steps. A reload re-attaches by the same path.
 * - On load, the thread comes from the API and `activeRunId` says whether a turn is in flight; if one is,
 *   it is watched from sequence zero and the stream replays into the transcript.
 * - Stop cancels the run rather than closing anything: a closed tab does not stop a turn, so neither
 *   does navigating away.
 *
 * <b>The agent asks its questions in prose and the turn ends.</b> There is no blocking question card any
 * more: a coding agent runs as a process of its own, so a tool that waited for an answer would have to
 * reach back into this app from inside a sandbox. The answer is the person's next message, which is also
 * what the transcript reads like afterwards. See `docs/agent-plan.md` for the MCP bridge that would make
 * it a tool again.
 */
@Component({
  selector: 'app-site-chat',
  imports: [FormsModule, Icon],
  templateUrl: './site-chat.html',
})
export class SiteChat {
  readonly siteNanoid = input.required<string>();

  /** Raised when a turn commits a version, so the editor can refresh its preview and its header. */
  readonly versionCommitted = output<string>();

  /** Raised while a workspace is starting, so the preview can say so instead of showing a dead frame. */
  readonly workspaceProgress = output<string>();

  /**
   * Raised when a turn ends, however it ended. The editor re-reads the site on it — a turn that wrote
   * nothing still left a warm workspace behind, and that is what the preview needs to know.
   */
  readonly turnFinished = output<void>();

  private readonly chat = inject(ChatService);
  private readonly realtime = inject(RealtimeService);

  protected readonly entries = signal<ChatEntry[]>([]);
  protected readonly enabled = signal(true);
  protected readonly running = signal(false);
  protected readonly error = signal<string | undefined>(undefined);
  protected readonly connected = this.realtime.connected;

  protected message = '';

  private runId?: string;

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  constructor() {
    // Reloads the thread whenever the editor switches to a different site. An effect rather than an
    // ngOnInit, because the shell keeps this component alive across that switch.
    effect(() => {
      const nanoid = this.siteNanoid();
      void this.load(nanoid);
    });
  }

  private async load(siteNanoid: string): Promise<void> {
    this.entries.set([]);
    this.enabled.set(await this.chat.enabled(siteNanoid));

    if (!this.enabled()) {
      return;
    }

    try {
      const conversation = await this.chat.conversation(siteNanoid);

      this.entries.set(
        conversation.messages.map((message: ChatMessageResponse) => ({
          kind: KIND_OF_ROLE[message.role] ?? 'assistant',
          text: message.text,
          versionNanoid: message.producedVersionNanoid ?? undefined,
        })),
      );

      // A turn that is still running: re-attach and let the replay finish the transcript.
      if (conversation.activeRunId) {
        await this.attach(conversation.activeRunId);
      }
    } catch (failure) {
      this.error.set(messageOf(failure));
    }
  }

  protected async send(): Promise<void> {
    const text = this.message.trim();

    if (!text || this.running()) {
      return;
    }

    this.message = '';
    this.error.set(undefined);
    this.append({ kind: 'user', text });

    try {
      const runId = await this.realtime.startChat(this.siteNanoid(), text);
      await this.attach(runId);
    } catch (failure) {
      this.error.set(messageOf(failure));
      this.running.set(false);
    }
  }

  /** The only thing that stops a turn. See `RunRegistry.TryCancel`. */
  protected async stop(): Promise<void> {
    if (this.runId) {
      await this.realtime.cancel(this.runId);
    }
  }

  private async attach(runId: string): Promise<void> {
    this.runId = runId;
    this.running.set(true);

    const events = await this.realtime.watch('Chat', runId);
    events.subscribe({ next: event => this.apply(event) });
  }

  private apply(event: RunEvent): void {
    switch (event.type) {
      case 'TextDelta':
        this.appendToAssistant(event.text ?? '');
        break;

      case 'MessageCompleted':
        // The whole message, which replaces whatever the deltas built: a client that joined mid-turn has
        // the tail and not the head, and rebuilding from deltas is more fragile than being told.
        this.replaceAssistant(event.text ?? '');
        break;

      case 'WorkspaceProgress':
        // Replaced rather than appended: this is one step with several stages, and a line per stage reads
        // like something going wrong.
        this.replaceLatest('waking', event.detail ?? 'Waking up your site');
        this.workspaceProgress.emit(event.detail ?? '');
        break;

      case 'Activity':
        this.append({ kind: 'activity', text: event.detail ?? 'Working' });
        break;

      case 'FileChanged':
        // Collected into one growing entry. A turn touches a dozen files and a chip each would bury the
        // sentence that explains them.
        this.addPath(event.detail ?? '');
        break;

      case 'VersionCommitted':
        this.append({
          kind: 'version',
          text: event.detail ?? 'Site updated',
          versionNanoid: event.versionNanoid ?? undefined,
        });
        this.versionCommitted.emit(event.versionNanoid ?? '');
        break;

      case 'BuildFailed':
        // Shown, not swallowed. The person's next message is what fixes it, and they can only write that
        // message if they can see what broke.
        this.append({ kind: 'build', text: event.detail ?? 'The site is not compiling.' });
        break;

      case 'Failed':
        this.append({ kind: 'error', text: event.error ?? 'Something went wrong.' });
        this.finish();
        break;

      case 'Completed':
        this.finish();
        break;
    }

    this.scrollToEnd();
  }

  private finish(): void {
    this.running.set(false);
    this.turnFinished.emit();

    if (this.runId) {
      void this.realtime.unwatch(this.runId);
      this.runId = undefined;
    }
  }

  private append(entry: ChatEntry): void {
    this.entries.update(entries => [...entries, entry]);
  }

  /** Streams into the last assistant entry, starting one if the last line is somebody else's. */
  private appendToAssistant(text: string): void {
    this.entries.update(entries => {
      const last = entries.at(-1);

      if (last?.kind === 'assistant') {
        return [...entries.slice(0, -1), { ...last, text: last.text + text }];
      }

      return [...entries, { kind: 'assistant', text }];
    });
  }

  private replaceAssistant(text: string): void {
    this.entries.update(entries => {
      const last = entries.at(-1);

      return last?.kind === 'assistant'
        ? [...entries.slice(0, -1), { ...last, text }]
        : [...entries, { kind: 'assistant', text }];
    });
  }

  /** Updates the trailing entry of a kind, or adds one. For the two entries that are a status, not a line. */
  private replaceLatest(kind: ChatEntry['kind'], text: string): void {
    this.entries.update(entries => {
      const last = entries.at(-1);

      return last?.kind === kind
        ? [...entries.slice(0, -1), { ...last, text }]
        : [...entries, { kind, text }];
    });
  }

  private addPath(path: string): void {
    if (!path) {
      return;
    }

    this.entries.update(entries => {
      const last = entries.at(-1);

      if (last?.kind === 'files') {
        const paths = last.paths ?? [];

        return paths.includes(path)
          ? entries
          : [...entries.slice(0, -1), { ...last, paths: [...paths, path] }];
      }

      return [...entries, { kind: 'files', text: '', paths: [path] }];
    });
  }

  private scrollToEnd(): void {
    const element = this.scroller()?.nativeElement;

    if (element) {
      element.scrollTop = element.scrollHeight;
    }
  }
}
