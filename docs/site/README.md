# Cadder documentation site

This workspace contains the Astro Starlight documentation source for Cadder.

## Commands

Run commands from `docs/site`.

```sh
bun install --frozen-lockfile
bun run dev
bun run check
bun run build
bun run preview
```

`bun run build` writes generated output to `docs/site/dist`. Do not commit generated output, `.astro`, cache directories, or preview artifacts.

## Content source

The durable architecture notes in `../ARCHITECTURE.md` remain the compact source for process boundaries and runtime behavior. The Starlight pages migrate that material into user-facing documentation and link back to the original file where useful.

## CI handoff

The repository CI should build documentation from source on `main` with Bun:

```sh
cd docs/site
bun install --frozen-lockfile
bun run check
bun run build
```

CI may publish or upload `docs/site/dist`, but it should not write generated site output back to the repository.
