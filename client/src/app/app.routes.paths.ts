/**
 * Centralized, typed route paths — build URLs through these, never with raw strings.
 *
 * The shape follows the product: everything that is not authentication or the site list lives under
 * one site, because every screen in Webly is about a site and the editor is where people are.
 */
export const AppRoutes = {
  /** The site list, which is also where a person with no site is offered one. */
  home: {
    path: '',
    build: () => '/',
  },
  login: {
    path: 'login',
    build: (redirect?: string) =>
      redirect ? `/login?redirect=${encodeURIComponent(redirect)}` : '/login',
  },
  register: {
    path: 'register',
    build: (redirect?: string) =>
      redirect ? `/register?redirect=${encodeURIComponent(redirect)}` : '/register',
  },
  verifyEmail: {
    path: 'verify-email',
    /**
     * `redirect` is where onboarding continues afterwards. The `token` the page also reads is never
     * built here: it only ever arrives from a link the backend put in an email.
     */
    build: (redirect?: string) =>
      redirect ? `/verify-email?redirect=${encodeURIComponent(redirect)}` : '/verify-email',
  },
  forgotPassword: {
    path: 'forgot-password',
    build: () => '/forgot-password',
  },
  resetPassword: {
    path: 'reset-password',
    build: (token?: string) =>
      token ? `/reset-password?token=${encodeURIComponent(token)}` : '/reset-password',
  },
  account: {
    path: 'account',
    build: () => '/account',
  },

  /** The first-site screen, shown full-screen as the last step of onboarding. */
  newSite: {
    path: 'new',
    build: () => '/new',
  },

  /**
   * The editor: the chat and the preview, side by side. The default child of a site, because it is
   * what somebody opening their site came to do.
   */
  site: {
    path: 'sites/:nanoid',
    build: (nanoid: string) => `/sites/${encodeURIComponent(nanoid)}`,
  },
  siteHistory: {
    path: 'sites/:nanoid/history',
    /** Children of the site shell, so the header and the site switcher stay put while these change. */
    childPath: 'history',
    build: (nanoid: string) => `/sites/${encodeURIComponent(nanoid)}/history`,

    /**
     * The chat links a committed version as `history?version={nanoid}` — a query parameter rather than a path
     * segment, because the screen is the list with one of its rows selected rather than a different screen,
     * and a link carrying a version that no longer exists should still open the history. It is built in the
     * template with `[routerLink]` and `[queryParams]`, so there is no second spelling of it here.
     */
  },
  /** The source, read-only. "You never have to touch the code" is not "you may not see it". */
  siteCode: {
    path: 'sites/:nanoid/code',
    childPath: 'code',
    build: (nanoid: string) => `/sites/${encodeURIComponent(nanoid)}/code`,
  },
  siteDomains: {
    path: 'sites/:nanoid/domains',
    childPath: 'domains',
    build: (nanoid: string) => `/sites/${encodeURIComponent(nanoid)}/domains`,
  },
  siteSettings: {
    path: 'sites/:nanoid/settings',
    childPath: 'settings',
    build: (nanoid: string) => `/sites/${encodeURIComponent(nanoid)}/settings`,
  },
} as const;
