import { Component, ElementRef, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Icon } from '../../shared/icon';
import { ChatService } from '../../services/chat.service';
import { RealtimeService, RunEvent } from '../../services/realtime.service';
import { messageOf } from '../../models/problem-details';
import { ChatMessageResponse } from '../../api/models/chat-message-response';

/** A line in the transcript. One shape for everything the stream can produce. */
export interface ChatEntry {
  kind: 'user' | 'assistant' | 'tool' | 'question' | 'version' | 'error';
  text: string;
  /** Tool chips and questions carry a little more. */
  options?: string[];
  questionId?: string;
  answered?: string;
  versionNanoid?: string;
}

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
 * - A question blocks the run. The card stays in the transcript, answered, so a replay shows it resolved.
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
          kind: message.role === 'User' ? 'user' : 'assistant',
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

  protected async answer(entry: ChatEntry, answer: string): Promise<void> {
    if (!this.runId || !entry.questionId) {
      return;
    }

    await this.realtime.answer(this.runId, entry.questionId, answer);

    // Marked here as well as on the stream's own QuestionAnswered event: the person who just clicked
    // should see it take effect now, not after a round trip.
    this.patch(entry, { answered: answer });
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

      case 'ToolCall':
        this.append({ kind: 'tool', text: event.detail ?? 'Working' });
        break;

      case 'ToolResult':
        // Deliberately not shown. A result line per call turns the chat into a log; the chip that is
        // already there is what the person needs, and the outcome is visible in the preview.
        break;

      case 'QuestionAsked':
        this.append({
          kind: 'question',
          text: event.text ?? '',
          options: event.options ?? [],
          questionId: event.questionId ?? undefined,
        });
        break;

      case 'QuestionAnswered':
        this.answerById(event.questionId, event.text ?? '');
        break;

      case 'VersionCommitted':
        this.append({ kind: 'version', text: event.detail ?? 'Site updated', versionNanoid: event.versionNanoid ?? undefined });
        this.versionCommitted.emit(event.versionNanoid ?? '');
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

  private answerById(questionId: string | null | undefined, answer: string): void {
    if (!questionId) {
      return;
    }

    this.entries.update(entries =>
      entries.map(entry => (entry.questionId === questionId ? { ...entry, answered: answer } : entry)),
    );
  }

  private patch(target: ChatEntry, changes: Partial<ChatEntry>): void {
    this.entries.update(entries => entries.map(entry => (entry === target ? { ...entry, ...changes } : entry)));
  }

  private scrollToEnd(): void {
    const element = this.scroller()?.nativeElement;

    if (element) {
      element.scrollTop = element.scrollHeight;
    }
  }
}
