# syntax=docker/dockerfile:1

# The sandbox image: one machine per open site, where the coding agent actually works.
#
# Build context is the REPOSITORY ROOT, because this image carries two things from it — the sandbox
# agent (tools/sandbox-agent) and the site template's dependency manifest (templates/next-site).
# Build it as `docker build -f deploy/sandbox/Dockerfile -t byc0/margareta:webly_sandbox .`.
#
# What is in here and why:
#
#   * **node, git and tar.** node runs the site and the agent CLIs. tar is the file transport
#     (tools/sandbox-agent uses it rather than a library so the image needs no npm install). git is
#     for the agent CLIs, which look for a repository to diff against and behave oddly without one —
#     note that this repository is *not* the site's history: Webly never gives a sandbox its real
#     repository or a credential that could write to it, and commits what comes back instead. See
#     ISiteRepositoryStore.
#   * **The template's dependencies, pre-installed.** This is what makes a cold workspace fast: the
#     seeding step runs `npm ls --depth=0` and only installs when the tree's lockfile asks for
#     something this image does not already have (SiteWorkspaceRegistry.SeedAsync). Without it every
#     first message in a session would wait on a full npm install.
#   * **Nothing of Webly's.** No connection string, no provider key, no session cookie. The only
#     credential that ever reaches this container is the agent's own model API key, passed per run as
#     an environment variable on the command, plus the per-sandbox bearer token below.
FROM node:22-bookworm-slim

# tar and ca-certificates are what the agent contract and every HTTPS call need; git is for the CLIs.
# One apt layer, no recommends, index deleted afterwards — this image is pulled per sandbox start in
# some providers, so its size is a startup cost rather than a download cost.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates git tar \
    && rm -rf /var/lib/apt/lists/*

# The two coding agents and the Vercel CLI, pinned. Pinned because an agent CLI's flags are part of
# this repository's code — ClaudeCodeAgent passes --output-format stream-json and parses what comes
# back, and "latest" silently changing that shape is a broken product with no diff to point at.
# Upgrading is a version bump here and a re-read of the parser.
ARG CLAUDE_CODE_VERSION=2.1.278
ARG OPENCODE_VERSION=1.18.31
ARG VERCEL_CLI_VERSION=59.23.2
RUN npm install --global --no-audit --no-fund \
      "@anthropic-ai/claude-code@${CLAUDE_CODE_VERSION}" \
      "opencode-ai@${OPENCODE_VERSION}" \
      "vercel@${VERCEL_CLI_VERSION}" \
    && npm cache clean --force

# The sandbox agent. Copied rather than installed: it has no dependencies, which is the point of it.
COPY tools/sandbox-agent/ /opt/webly-agent/

# The template's dependencies, baked into the workspace directory itself rather than somewhere they
# would have to be copied or symlinked from. `npm ci` against the committed lockfile, so this image
# holds exactly the versions a new site starts with; the manifests are copied alone first so editing
# the template's source does not invalidate this layer.
WORKDIR /workspace
COPY templates/next-site/package.json templates/next-site/package-lock.json ./
RUN npm ci --no-audit --no-fund

# Next.js writes its build output and its dev-server cache under the workspace, and telemetry on
# first run prints a notice into the output the agent's parser is reading.
ENV NEXT_TELEMETRY_DISABLED=1
ENV WEBLY_WORKSPACE=/workspace
ENV WEBLY_AGENT_PORT=8080
ENV WEBLY_DEV_PORT=3000

# WEBLY_AGENT_TOKEN is deliberately not defaulted. The agent refuses every request without a token,
# and a default would be a shared secret baked into a published image — the provider mints one per
# sandbox and passes it in (see DockerSandboxProvider and E2bSandboxProvider).
EXPOSE 8080

# Non-root. node's own account, and the workspace is handed to it: the agent writes the site's files
# and runs npm as this user, so nothing in a turn needs root and a container escape starts from less.
RUN chown -R node:node /workspace /opt/webly-agent
USER node

# No healthcheck: the provider polls GET /health itself and has to, because it is the thing that
# decides when a sandbox is usable. A HEALTHCHECK here would be a second opinion the provider ignores.
CMD ["node", "/opt/webly-agent/index.js"]
