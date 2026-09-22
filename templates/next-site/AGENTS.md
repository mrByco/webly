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
9. **A new page needs a link to it**, from the header or from wherever somebody would look for it. A page
   nothing links to is a page nobody visits. The sitemap takes care of itself — it reads the folders.
10. **Never write a form by hand, and never write a server for one.** Use `ContactForm` from
    `src/components/contact-form.tsx` — it is already wired to Webly, which stores the message, emails the
    owner and sends the visitor back. This project is built as a **static export**: a route handler, a server
    action or an API route will compile and then 404 on the published site, and the enquiry it was supposed to
    receive is simply lost. Change the fields, the labels and the copy; keep the hidden fields as the component
    sets them, honeypot included, and give the email field a name containing "email" so the owner can reply.
    **Name every field for a person to read** — `preferred-date`, not `f3`: a browser posts field names and
    never the labels beside them, so the name is what appears above the answer in the owner's messages.
    A `mailto:` link is not a substitute: it opens whatever the visitor's device thinks is a mail client, which
    on a phone is often nothing at all.

    **Keep the form inside `SentNotice`.** It is what tells the visitor the message arrived — Webly sends them
    back to the page they were on, so without it they land on an empty form with nothing saying it worked, and
    the next thing they do is send it twice or give up.
11. **Use the pictures they gave you, and no others.** Anything in `public/images/` is a photograph the owner
    uploaded; refer to it by the path without `public` — `/images/shopfront.jpg` — and give every one real alt
    text describing what is in it. Do **not** link to an image on another website, do not use a stock-photo
    service, and do not invent a path: a missing image is a broken page, and somebody else's photograph on a
    real business's site is a copyright problem with their name on it. If a page needs a picture nobody has
    uploaded, say so in your reply and lay the page out without it.

## Check your work before you finish

Run `npm run typecheck` as your last step, and fix whatever it reports.

It takes a few seconds, and **Webly runs it again after your turn ends**. Whatever it says then goes straight
into the chat, as a block telling the person their site is not compiling — so a type error you leave behind is
not invisible, it is a message saying you broke their website. Fixing it now costs you a minute; leaving it
costs them a scare and another turn.

The dev server cannot help you here: it compiles with SWC, which strips types instead of checking them, so a
page with a type error serves perfectly and its log says nothing. `npm run typecheck` is the only way to know.

If you changed a page's structure or copy, also look at the running dev server's output for a compile error
before you finish. It is already compiling while you work, and that is where a syntax error or a bad import
shows up.

## Shape of the project

- `src/app/` — routes. `page.tsx` is the home page; a new page is a folder with a `page.tsx`.
- `src/app/layout.tsx` — the shell: header, footer, fonts, metadata.
- `src/components/` — reusable pieces. Keep them small and typed.
- `src/app/look.css` — this site's colour and corner radius, as three numbers. "Make it green", "something
  warmer", "softer corners" are edits to **this file and nothing else**: the whole palette, neutrals included,
  is derived from the hue and chroma. Webly picks them when the site is created so that two new sites are not
  identical, and they are yours to change from the first message onwards.
- `src/app/icon.svg` — the tab icon. A coloured tile in the same brand colour, because nobody has given us a
  logo; it is a file rather than a variable, so its colour is the one place `look.css` cannot reach and has to
  be edited alongside it. Replace the whole file when the business has a mark of its own, and keep it square
  and legible at 16px.
- `src/app/opengraph-image.tsx` — the picture a shared link shows. Type on the brand colour, drawn by the
  build. Its colour is a **hex**, because the thing that draws it has no CSS engine and refuses `oklch()`;
  change it alongside `look.css` if you change the colour. Replace the whole file when the business has a
  photograph worth putting in front of a link.
- `src/app/globals.css` — Tailwind plus the tokens derived from `look.css`. Change *how* a token is derived
  here, once, rather than putting class names for one brand colour on forty elements. Two of those tokens are
  a pair worth knowing: a card's outline is `border-edge` and something you **type into** is
  `border-field-edge`, which is darker on purpose. A card is identified by what is in it, so a faint rule is
  enough; an empty field's border is the only thing saying where to click, and the accessibility standard asks
  for more contrast on exactly that. Use `border-field-edge` on every input, textarea and select you add.
- `content/brand.md` — the confirmed facts. Your source of truth for copy. **The owner reads this file**, on
  their settings screen, under "What Webly knows about your business" — so write it for them, not for you.
  Its **Name** is the same fact as `siteName` in `src/site.ts`: if they correct it, change both.
- `src/app/sitemap.ts` — `/sitemap.xml`. It reads the folders under `src/app` at build time, so a page you
  add is in it without you doing anything. Leave it alone.
- `src/site.ts` — the business's name, where this site lives as the build was told, and where its forms post.
  `siteName` is what the owner called the site when they created it; the header, the footer and every page's
  title read it, so correcting the name is an edit to that one line rather than to four components. Use
  `siteUrl` rather than writing an address anywhere: it changes the day somebody connects their own domain,
  and a canonical link pointing at the old one tells every search engine the real site is somewhere it is not.
- `src/components/contact-form.tsx`, `src/components/sent-notice.tsx` and `src/app/contact/page.tsx` — the
  working form, the confirmation the visitor sees afterwards, and the page both sit on. Rewrite the copy;
  leave the plumbing, and leave the form wrapped.
- `public/` — files served from the root of the site. `public/images/` is where the owner's photographs are,
  reached at `/images/…`. Next's image optimizer is off (a static export has no server to run it), so use
  `next/image` with `width` and `height` for the layout, or a plain `<img>`; either way the file is served as
  uploaded.

## What you must not touch

- `package.json` scripts, `next.config.ts`, `tsconfig.json` — the build contract Webly deploys with.
- `.env*` — there are no secrets in this project and it must stay that way.
- Anything outside this directory. There is nothing else there.
