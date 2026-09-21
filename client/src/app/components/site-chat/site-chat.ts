import { Component, ElementRef, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AppRoutes } from '../../app.routes.paths';
import { Icon } from '../../shared/icon';
import { AutoGrow } from '../../shared/auto-grow';
import { ChatService } from '../../services/chat.service';
import { Subscription } from 'rxjs';
import { RealtimeService, RunEvent } from '../../services/realtime.service';
import { messageOf } from '../../models/problem-details';
import { shrinkImage } from '../../models/image-file';
import { ImageService } from '../../services/image.service';
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
  imports: [FormsModule, Icon, RouterLink, AutoGrow],
  templateUrl: './site-chat.html',
  // The host element is a flex item of the editor's pane and has to fill it. Without this it is a plain
  // block that sizes to its content, and the `h-full` inside resolves against that — so the pane was
  // whatever height its contents happened to be and the rest was empty grey. Declared here rather than on
  // each usage, because every usage needs it and the template already assumes it.
  host: { class: 'flex min-h-0 flex-1 flex-col' },
})
export class SiteChat {
  readonly siteNanoid = input.required<string>();

  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly routes = AppRoutes;

  /**
   * What the empty thread offers. Requests, not facts: each one is a thing to ask for, and the agent asks back
   * for whatever it needs to do it — which is the product's own answer to "where do the facts come from".
   */
  protected readonly suggestions = [
    'Say what we do on the home page',
    'Add our address and opening hours',
    'Make the tone warmer',
  ];

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
  private readonly images = inject(ImageService);
  private readonly realtime = inject(RealtimeService);

  protected readonly entries = signal<ChatEntry[]>([]);
  protected readonly enabled = signal(true);
  protected readonly running = signal(false);
  protected readonly error = signal<string | undefined>(undefined);
  protected readonly uploading = signal(false);
  protected readonly connected = this.realtime.connected;

  protected message = '';

  private runId?: string;

  /**
   * The subscription to the watched run, held so that leaving can end it.
   *
   * Dropping it on the floor is what made switching sites mid-turn write one site's turn into another's
   * chat — see `detach`.
   */
  private events?: Subscription;

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
    // Before anything else: this component survives a switch between sites, and what it was watching belongs
    // to the site being left.
    await this.detach();

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

  /**
   * Puts a suggestion in the box rather than sending it, and focuses so the caret is where the next word goes.
   *
   * The textarea is found in the DOM rather than with a `viewChild`: it lives inside an `@if` on whether this
   * deployment has an agent at all, and that query resolved to undefined — see the editor's tab row, which has
   * the same shape and the same comment.
   */
  protected suggest(text: string): void {
    this.message = text;

    const composer = (this.host.nativeElement as HTMLElement).querySelector('textarea');

    composer?.focus();
    composer?.setSelectionRange(text.length, text.length);
  }

  /**
   * Puts the person's own photographs into their site, from the chat.
   *
   * <b>Here rather than on a screen of its own</b>, because an upload is never the thing somebody wanted: they
   * wanted the picture *on* a page, and the sentence that says which page is the next thing they type. So the
   * files are committed and their paths land in the composer, with the caret after them — the message they
   * send is "/images/shopfront.jpg at the top of the home page", which is exactly what the agent needs.
   *
   * The upload is a version like any other, so the preview is told to reload: the files are in the site's
   * source the moment this returns, and an agent turn is not needed to make them exist.
   */
  protected async addImages(input: HTMLInputElement): Promise<void> {
    const chosen = [...(input.files ?? [])];

    // Cleared immediately, so choosing the same file twice in a row still fires a change event.
    input.value = '';

    if (chosen.length === 0 || this.uploading()) return;

    this.uploading.set(true);
    this.error.set(undefined);

    try {
      // Shrunk in the browser: a phone's photograph is several megabytes and a published site has no image
      // optimizer behind it, so what is uploaded is what every visitor downloads. See `models/image-file.ts`.
      const files = await Promise.all(chosen.map(shrinkImage));
      const result = await this.images.upload(this.siteNanoid(), files);
      const urls = result.images.map(image => image.url);

      this.entries.update(entries => [
        ...entries,
        { kind: 'files', text: urls.length === 1 ? 'Added an image' : `Added ${urls.length} images`, paths: urls },
      ]);

      this.suggest(`${this.message.trim()} ${urls.join(' ')} `.trimStart());

      if (result.versionNanoid) this.versionCommitted.emit(result.versionNanoid);
    } catch (failure) {
      this.error.set(messageOf(failure));
    } finally {
      this.uploading.set(false);
    }
  }

  /**
   * Enter sends; Shift+Enter writes a second line.
   *
   * It is bound rather than inherited because the composer is a `<textarea>`, and a textarea does not submit
   * its form on Enter — an `<input>` does. This one was an input until it had to grow with the message, so
   * changing it silently took away the only way to send with the keyboard: the whole interface of this product
   * is a chat box, and pressing Enter in it put the caret on a blank second line and did nothing. Nothing
   * catches that — the template type-checks, the page renders, the screenshot looks right — and it was found
   * by a script that pressed Enter and waited for something to happen.
   *
   * Angular's `keydown.enter` already excludes Shift, Alt, Ctrl and Meta, so the newline case needs no code.
   * `isComposing` does: while an input method is offering candidates, Enter accepts one, and a message sent
   * mid-word is worse than one Enter that does nothing.
   */
  protected onEnter(event: Event): void {
    if ((event as KeyboardEvent).isComposing) return;

    event.preventDefault();

    void this.send();
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

    // Kept, and the previous one ended first. `watch` hands back the *same* stream for a run it is already
    // watching, so subscribing twice to it is not a second stream — it is every event applied twice, which is
    // how coming back to a site mid-turn drew its last two entries in duplicate.
    this.events?.unsubscribe();
    this.events = events.subscribe({ next: event => this.apply(event) });
  }

  /**
   * Stops watching whatever this was watching, without stopping it.
   *
   * Switching sites keeps this component alive — the effect in the constructor reloads it — so without this
   * the run belonging to the site being left went on writing into the new site's transcript: its "waking up
   * your site" line appeared under somebody else's history, and the composer offered a Stop button that would
   * have cancelled a turn on a site that was no longer on screen. The run itself is untouched, which is the
   * point: a turn outlives the page looking at it, and coming back re-attaches through `activeRunId` exactly
   * as a reload does.
   */
  private async detach(): Promise<void> {
    this.events?.unsubscribe();
    this.events = undefined;
    this.running.set(false);

    const runId = this.runId;
    this.runId = undefined;

    if (runId) await this.realtime.unwatch(runId);
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
        // A detail on the terminal is what the app has to say about how the turn ended — today only
        // "Stopped. Nothing was changed." A notice rather than an assistant bubble, because the agent did not
        // say it; and shown here rather than left to the thread, because the thread is only re-read on a
        // reload, so without this the person who pressed Stop was left looking at a status line that had
        // stopped moving.
        if (event.detail) {
          this.append({ kind: 'notice', text: event.detail });
        }

        this.finish();
        break;
    }

    this.scrollToEnd();
  }

  private finish(): void {
    this.running.set(false);
    this.turnFinished.emit();

    this.events?.unsubscribe();
    this.events = undefined;

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
