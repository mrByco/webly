#!/usr/bin/env node
/**
 * Regenerates `src/app/api/` from the running backend's swagger.
 *
 * One script for every platform, and — the reason it is a script at all — one that **trusts the development
 * certificate rather than turning verification off**. This used to be three npm scripts, two of them setting
 * `NODE_TLS_REJECT_UNAUTHORIZED=0` because Kestrel's dev certificate is self-signed and node has its own CA
 * list that the machine's trust store is not part of. That flag disables certificate verification for the
 * whole process — every request, not just the one to localhost — and it is exactly the habit that is
 * dangerous somewhere it is copied to later. `dotnet dev-certs` can export the certificate, and node can be
 * told about one more CA, so the generator verifies the backend like anything else.
 *
 * The export is idempotent and lands in `.run/`, which is gitignored: it is the same certificate the browser
 * is already being asked to trust, not a new secret.
 */
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const client = dirname(fileURLToPath(import.meta.url));
const repository = resolve(client, '..');
const certificate = join(repository, '.run', 'certs', 'webly-dev.crt');

function exportCertificate() {
    mkdirSync(dirname(certificate), { recursive: true });

    const result = spawnSync(
        'dotnet',
        ['dev-certs', 'https', '--export-path', certificate, '--format', 'PEM', '--no-password'],
        { stdio: 'pipe', encoding: 'utf8', shell: process.platform === 'win32' });

    // Not fatal on its own. A machine with no .NET SDK can still have a backend to generate from — someone
    // else's, or one in a container — and if the certificate is genuinely needed the fetch below says so
    // with a TLS error, which is a better message than this one would be.
    if (result.status !== 0 && !existsSync(certificate))
        console.warn(
            `Could not export the development certificate (${(result.stderr || result.error?.message || '').trim()}).\n`
            + 'If generation fails with a certificate error, run `dotnet dev-certs https --trust` and retry.');
}

exportCertificate();

const extraCas = existsSync(certificate) ? { NODE_EXTRA_CA_CERTS: certificate } : {};

// Anything else on the command line goes straight through, which is how a caller overrides the swagger URL:
// `node regen-api.mjs --input https://…/swagger/v1/swagger.json`. That used to mean writing a temporary copy
// of the config file, because the config's `input` was the only way in.
const generate = spawnSync(
    'npx',
    ['ng-openapi-gen', '--config', 'open-api-gen.json', ...process.argv.slice(2)],
    {
        cwd: client,
        stdio: 'inherit',
        shell: process.platform === 'win32',
        env: { ...process.env, ...extraCas }
    });

process.exit(generate.status ?? 1);
