import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { apiSitesSiteNanoidChatGet$Json } from '../api/fn/chat/api-sites-site-nanoid-chat-get-json';
import { apiSitesSiteNanoidChatArchivePost } from '../api/fn/chat/api-sites-site-nanoid-chat-archive-post';
import { apiSitesSiteNanoidChatStatusGet$Json } from '../api/fn/chat/api-sites-site-nanoid-chat-status-get-json';
import { ConversationResponse } from '../api/models/conversation-response';

/**
 * The chat's non-streaming half: load the thread, start a new one, ask whether the agent exists here
 * at all. Sending a message goes over the hub — see `RealtimeService`.
 */
@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly api = inject(Api);

  conversation(siteNanoid: string): Promise<ConversationResponse> {
    return this.api.invoke(apiSitesSiteNanoidChatGet$Json, { siteNanoid });
  }

  archive(siteNanoid: string): Promise<void> {
    return this.api.invoke(apiSitesSiteNanoidChatArchivePost, { siteNanoid });
  }

  /** False in a deployment with no provider keys. The editor hides the chat rather than failing in it. */
  async enabled(siteNanoid: string): Promise<boolean> {
    try {
      const status = await this.api.invoke(apiSitesSiteNanoidChatStatusGet$Json, { siteNanoid });

      return status.enabled;
    } catch {
      return false;
    }
  }
}
