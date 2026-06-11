---
id: TASK-1.22
title: Polish Cadder documentation for user-first portable usage
status: Done
assignee:
  - '@agent'
created_date: '2026-06-11 10:04'
updated_date: '2026-06-11 11:23'
labels: []
dependencies: []
references:
  - TASK-1.16
  - assets/banner.webp
  - assets/logo.png
  - README.md
  - docs/site/astro.config.mjs
  - docs/site/src/content/docs/index.mdx
documentation:
  - docs/site/README.md
  - docs/ARCHITECTURE.md
  - 'https://diataxis.fr/'
  - 'https://starlight.astro.build/guides/customization/'
  - 'https://starlight.astro.build/guides/css-and-tailwind/'
  - 'https://developers.google.com/style/code-syntax'
  - 'https://clig.dev/'
  - 'https://caddyserver.com/docs/'
modified_files:
  - README.md
  - assets/banner.webp
  - assets/logo.png
  - docs/site/astro.config.mjs
  - docs/site/public/cadder-downloads.js
  - docs/site/public/favicon.ico
  - docs/site/src/assets/banner.webp
  - docs/site/src/assets/logo.png
  - docs/site/src/styles/cadder.css
  - docs/site/src/content/docs/index.mdx
  - docs/site/src/content/docs/guides/cadder-toml.mdx
  - docs/site/src/content/docs/guides/getting-started.mdx
  - docs/site/src/content/docs/guides/how-to-use.mdx
  - docs/site/src/content/docs/guides/path-and-shim.mdx
  - docs/site/src/content/docs/guides/tui-diagnostics.mdx
  - docs/site/src/content/docs/guides/windows.mdx
  - docs/site/src/content/docs/guides/macos.mdx
  - docs/site/src/content/docs/guides/linux.mdx
  - docs/site/src/content/docs/reference/real-caddy-resolution.mdx
  - docs/site/src/content/docs/reference/runtime-configuration.mdx
  - docs/site/src/content/docs/guides/portable-binaries.mdx
  - docs/site/src/content/docs/guides/release-process.mdx
  - docs/site/src/content/docs/guides/validation.mdx
  - docs/site/src/content/docs/reference/architecture.mdx
parent_task_id: TASK-1
priority: medium
ordinal: 22000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Rework the Cadder documentation site and README so public documentation leads with using released portable binaries, Cadder branding, OS-specific setup cookbooks, and practical user workflows. Developer build, validation, and release material should remain available but be clearly separated from the main user path. The implementation must preserve the actual real Caddy resolver behavior while promoting `cadder.toml` next to the Cadder binary as the recommended user-facing configuration path.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Docs header shows the Cadder logo and the Starlight theme uses colors consistent with `assets/banner.webp`.
- [x] #2 The first documentation page explains what Cadder is, what binaries it includes, and points users to the release artifact workflow before developer build workflows.
- [x] #3 Public user documentation and README examples do not use `.local` as an example path.
- [x] #4 User documentation is separated from developer documentation in navigation and page content.
- [x] #5 Windows, macOS, and Linux cookbooks cover portable binary use, optional user-managed PATH shim setup, recommended `cadder.toml`, TUI diagnostics, and common failure recovery.
- [x] #6 The recommended configuration path promotes `cadder.toml` next to the binary first, per-project `cadder.toml` second, and CLI/env/PATH methods as advanced alternatives while the resolver reference preserves the actual precedence order.
- [x] #7 Both environment aliases, `CADDER_CADDY_REAL_COMMAND` and `CADDER_CADDY__REAL_COMMAND`, are documented in the reference or advanced configuration material.
- [x] #8 Documentation validation passes with Bun check/build, and the docs are inspected in a browser on desktop and mobile viewports.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Implementation plan

1. Keep the existing Rust behavior and configuration format unchanged. Treat `cadder.toml` as the only documented config file name.
2. Add Cadder branding to the Starlight site by placing docs-site-owned copies of the existing logo/banner assets under `docs/site/src/assets` or `docs/site/public`, configuring `logo` in `astro.config.mjs`, and adding a custom CSS file for the dark teal/cyan theme.
3. Reorganize the Starlight sidebar into `Start here`, `User guide`, `Cookbooks`, `Reference`, and `Developer`, keeping existing public slugs where practical and moving validation/release material into the Developer group.
4. Rewrite user-facing docs and README so the primary path is: download a GitHub Release archive, unpack it, configure `cadder.toml` next to the binary, run the shim, then inspect with `cadder-tui`.
5. Replace `.local` examples in public docs with neutral user paths such as `$HOME/Tools/Cadder`, `$HOME/bin`, `C:\Tools\Cadder`, or `dist/cadder` for contributor-only build output.
6. Convert the existing Windows, macOS, and Linux guides into cookbook-style setup pages that cover direct portable use, optional user-managed PATH shim setup, recommended `cadder.toml`, TUI diagnostics, and recovery for common real-Caddy resolution failures.
7. Update real Caddy configuration/reference docs to distinguish recommended documentation order from actual resolver precedence, and document both `CADDER_CADDY_REAL_COMMAND` and `CADDER_CADDY__REAL_COMMAND` as advanced aliases.
8. Validate with `bun install --frozen-lockfile`, `bun run check`, `bun run build`, `rg -n "\.local" README.md docs/site/src/content/docs`, browser inspection on desktop and mobile, and any relevant project checks if changes go beyond docs/README.

## Review feedback fix plan

1. Preserve the user-adjusted color palette in `docs/site/src/styles/cadder.css`; only add structural styles if needed for download buttons or layout.
2. Rename the sidebar group `Start here` to `Quick Start` and remove the Developer sidebar section.
3. Generate favicon files from `docs/site/src/assets/logo.png` using the locally available image tooling and configure Starlight/Astro to use the logo-derived favicon.
4. Rewrite Overview and Getting Started to remove over-explained archive/folder/Rust guidance, explain the port/reverse-proxy motivation accurately, add a concise feature list, add OS download buttons linked to latest release assets, and remove `Good first reads`.
5. Rename the user guide page from `Portable binaries` to a less pretentious `How to use` page while preserving the existing slug where practical.
6. Add a dedicated `cadder.toml` user page and replace repeated config snippets in user-facing pages with links to that page.
7. Remove implementation-library and internal-repo details from user-facing docs, including Ratatui/Crossterm, manual smoke test references, `directories::ProjectDirs`, and architecture migration wording.
8. Simplify README to project overview, license/contributing, quick use, and documentation links instead of duplicating most docs content.
9. Validate with docs check/build, grep for unwanted public wording, browser inspection where useful, and update task notes/final summary.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented a user-first documentation polish for Cadder. The Starlight site now uses docs-site-owned copies of the Cadder logo and banner, a custom dark teal/cyan theme, and navigation split into Start here, User guide, Cookbooks, Reference, and Developer. README and user docs now lead with GitHub Release archives, portable binary use, `cadder.toml` next to the binaries, running the shim, and inspecting with `cadder-tui`. Build-from-source guidance was moved to a separate Developer page. Windows, macOS, and Linux cookbook pages now cover direct portable use, optional PATH setup, recommended `cadder.toml`, TUI diagnostics, and common recovery. Public docs no longer use `.local` as an example path. Real Caddy reference preserves actual resolver precedence and documents both `CADDER_CADDY_REAL_COMMAND` and `CADDER_CADDY__REAL_COMMAND`.

Validation: `bun install --frozen-lockfile`, `bun run check`, and `bun run build` passed in `docs/site`. `rg -n "\.local" README.md docs/site/src/content/docs` returned no matches. Browser inspection covered the docs homepage and key pages on desktop/mobile-style viewports; logo, banner, sidebar groups, cookbook content, and reference env aliases were present with no console errors, and a Lighthouse mobile navigation audit reported Accessibility 100, Best Practices 100, SEO 92, Agentic Browsing 94. `cargo run -p xtask -- check` passed. `cargo run -p xtask -- coverage` passed and wrote `target/llvm-cov/coverage-summary.json`; total line coverage is 86.93%, above the 85% threshold.

Addressed review feedback on the documentation polish. Preserved the existing color variables and only added layout CSS for download cards. Renamed `Start here` to `Quick Start`, removed the Developer/validation/release/architecture pages from the public Starlight navigation/content, replaced `Portable binaries` with `How to use`, added a dedicated `cadder.toml` page, and removed repeated TOML snippets from user-facing pages. The Overview now explains HTTP/HTTPS port contention and local reverse-proxy coordination, includes a feature list, and uses OS-specific download buttons. The buttons fall back to GitHub Releases and use a small public script to rewrite links to matching latest release assets when a release exists. Generated `favicon.ico` from the Cadder logo with ImageMagick and configured Starlight to use it. README was reduced to project overview, quick use, documentation links, commands, contribution/security/architecture links, and license.

Review cleanup removed wording called out in feedback, including `Start here`, `Portable binaries`, `.local`, `config.toml`, `Ratatui/Crossterm`, `directories::ProjectDirs`, manual smoke-test references, forced unpack/folder instructions, and Windows `.exe` dashboard examples from public README/docs source. Current GitHub Releases has no published release yet, so direct asset URLs cannot be resolved at authoring time; the docs use `releases?per_page=1` at runtime to avoid 404 console errors and keep OS buttons ready for the first published release.

Validation after fixes: `bun run check` passed, `bun run build` passed with 12 pages, grep for review-banned public wording returned no matches, `git diff --check` passed aside from Git LF/CRLF warnings, ImageMagick identified multi-size `docs/site/public/favicon.ico`, and browser checks on desktop/mobile verified the logo, favicon, download cards, mobile layout, no visible banned wording, and 0 console warnings/errors after the final reload.

Follow-up layout fix: changed the download button layout to explicit CSS grid areas: `windows macos-arm linux` and `windows macos-intel linux`, with Windows and Linux spanning both rows and a one-column mobile fallback. Verified with `bun run check`, `bun run build`, and Playwright computed styles on desktop (`gridTemplateAreas` matched the requested layout).

Closeout pre-commit check: root `assets/banner.webp` and `assets/logo.png` match the docs-site asset copies byte-for-byte by SHA-256, so the commit includes both root assets and Starlight-local copies to keep README and docs branding consistent.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Reworked the documentation feedback pass around a stricter user-first public site. The Starlight sidebar now starts with `Quick Start`, has no Developer section, and no longer publishes the previous validation, release-process, or architecture pages as public docs content. `Portable binaries` was replaced with `How to use`, and a dedicated `cadder.toml` page now owns the real-Caddy configuration examples.

The Overview now describes the real problem Cadder solves: local Caddy projects competing for HTTP/HTTPS ports while serving reverse proxies. It includes a feature list, the existing binary list, and OS-specific download buttons with platform icons. Because release assets are versioned and no GitHub Release exists yet, the buttons fall back to `releases/latest` and a small public script rewrites them to matching latest release asset URLs when available. The docs favicon is generated from the Cadder logo and Starlight is configured to use it.

README was trimmed to overview, quick use, documentation links, commands, contributing/security/architecture links, and license. User-facing pages no longer repeat `cadder.toml` snippets, no longer prescribe unpack folders, no longer include `.local`, `config.toml`, Ratatui/Crossterm, `directories::ProjectDirs`, manual smoke tests, or Windows `.exe` dashboard examples.

Checks run: `bun run check`, `bun run build`, grep for review-banned public wording, `git diff --check`, ImageMagick favicon identification, and browser verification on desktop/mobile with 0 console warnings/errors after the final reload.

Follow-up: adjusted the OS download buttons to use explicit grid areas so desktop renders as `Windows | macOS Apple Silicon | Linux` on the first row and `Windows | macOS Intel | Linux` on the second row, with Windows and Linux spanning both rows. Mobile keeps a single-column layout.
<!-- SECTION:FINAL_SUMMARY:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Tests or explicit verification were run for the changed behavior
- [x] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
