import { describe, expect, it } from 'vitest';

import { brandFacts } from './brand-facts';

describe('brandFacts', () => {
  it('reads the template file as headings, a paragraph and a list of bold labels', () => {
    const blocks = brandFacts(
      [
        '# About this business',
        '',
        'Everything in this file has been confirmed by the site\'s owner. Anything not in here is not known — ask',
        'before putting it on the website.',
        '',
        '## Facts',
        '',
        '- **Name:** Brightwater Florist',
        '- **Where:** Havenstraat 4, Haarlem',
      ].join('\n'),
    );

    expect(blocks).toEqual([
      { kind: 'heading', level: 1, text: 'About this business' },
      {
        kind: 'paragraph',
        parts: [
          {
            text: 'Everything in this file has been confirmed by the site\'s owner. Anything not in here is not known — ask before putting it on the website.',
            bold: false,
          },
        ],
      },
      { kind: 'heading', level: 2, text: 'Facts' },
      {
        kind: 'list',
        items: [
          [{ text: 'Name:', bold: true }, { text: ' Brightwater Florist', bold: false }],
          [{ text: 'Where:', bold: true }, { text: ' Havenstraat 4, Haarlem', bold: false }],
        ],
      },
    ]);
  });

  it('leaves out what was written for the agent rather than the owner', () => {
    const blocks = brandFacts('- **Name:** Joe\'s Garage\n  <!-- also siteName in src/site.ts -->\n');

    expect(blocks).toEqual([
      { kind: 'list', items: [[{ text: 'Name:', bold: true }, { text: ' Joe\'s Garage', bold: false }]] },
    ]);
  });

  it('keeps a wrapped bullet as one bullet', () => {
    const blocks = brandFacts('- **Hours:** Monday to Saturday,\n  9:00 to 18:00\n- **Phone:** 030 123 4567');

    expect(blocks).toEqual([
      {
        kind: 'list',
        items: [
          [{ text: 'Hours:', bold: true }, { text: ' Monday to Saturday,', bold: false }, { text: ' 9:00 to 18:00', bold: false }],
          [{ text: 'Phone:', bold: true }, { text: ' 030 123 4567', bold: false }],
        ],
      },
    ]);
  });

  it('turns links and code marks into their words, and markup into text', () => {
    const blocks = brandFacts('See [our menu](/menu) and `brand.md`. <script>alert(1)</script>');

    // Text, not HTML: the template binds these values, so the tag is shown rather than run.
    expect(blocks).toEqual([
      { kind: 'paragraph', parts: [{ text: 'See our menu and brand.md. <script>alert(1)</script>', bold: false }] },
    ]);
  });
});
