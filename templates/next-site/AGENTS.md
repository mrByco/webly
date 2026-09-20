# Working on this website

You are editing a real customer's website through Webly. They are not a developer: they will never read this
code, and they cannot fix anything you get wrong. What you commit is what their visitors see.

Read `content/brand.md` before writing any copy. It holds the facts about this business that somebody has
actually confirmed.

## Rules

1. **Never invent a fact.** Opening hours, prices, addresses, phone numbers, years in business, customer
   quotes, certifications, delivery areas — if it is not in `content/brand.md` or the conversation, you do not
   know it. Ask for it, in your reply, and leave the section out until you have it. A plausible invention on a
   real business's website is the worst thing this product can do.
2. **Never write a testimonial, review or statistic that nobody gave you.** Not even as a placeholder that
   "they can edit later". They will not edit it; it will be published.
3. **Keep the build working.** The dev server is running while you work — if you break the build, fix it before
   you finish. Do not finish a turn with a compile error.
4. **Stay in the stack.** Next.js App Router, TypeScript, Tailwind. Do not add a component library, a CMS, an
   ORM, an analytics script or a state manager. Do not add a dependency at all unless the request genuinely
   cannot be done without one, and say so in your reply if you do.
5. **Edit what is there.** A request for a new headline is not a request to restructure the home page.
6. **Write for their customers**, in the language the person writes to you in: what the reader gets, not how
   passionate the team is. Short sentences. No "unlock", no "seamless", no "we are excited to".
7. **Record what you learn.** When the person tells you a fact about their business, add it to
   `content/brand.md` in the same turn. The next session starts from that file, not from this conversation.
8. **Accessibility and responsiveness are not optional.** Real alt text, one `h1` per page, visible focus
   states, and every layout works at 390px.

## Check your work before you finish

Run `npm run typecheck` as your last step, and fix whatever it reports.

It takes a few seconds. The alternative is that the person presses Publish, the build fails, their site does
not go live, and the first they hear of it is an email saying so — which costs them a wait and you another
turn. A type error you introduced is the single most likely way for that to happen, and it is the one kind of
mistake a machine can catch for you.

If you changed a page's structure or copy, also look at the running dev server's output for a compile error
before you finish. It is already compiling while you work.

## Shape of the project

- `src/app/` — routes. `page.tsx` is the home page; a new page is a folder with a `page.tsx`.
- `src/app/layout.tsx` — the shell: header, footer, fonts, metadata.
- `src/components/` — reusable pieces. Keep them small and typed.
- `src/app/globals.css` — Tailwind plus the site's design tokens. Change colours and type **here**, once,
  rather than putting class names for one brand colour on forty elements.
- `content/brand.md` — the confirmed facts. Your source of truth for copy.

## What you must not touch

- `package.json` scripts, `next.config.ts`, `tsconfig.json` — the build contract Webly deploys with.
- `.env*` — there are no secrets in this project and it must stay that way.
- Anything outside this directory. There is nothing else there.
