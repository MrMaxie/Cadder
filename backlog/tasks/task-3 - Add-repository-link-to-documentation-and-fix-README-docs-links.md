---
id: TASK-3
title: Add repository link to documentation and fix README docs links
status: To Do
assignee: []
created_date: '2026-06-11 16:45'
labels:
  - documentation
milestone: m-2
dependencies: []
references:
  - README.md
  - docs/site/src/content/docs/index.mdx
documentation:
  - docs/site/README.md
priority: medium
ordinal: 27800
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Cadder's public documentation should make the GitHub repository easy to reach, and the root README should link readers to the published documentation site instead of Starlight source files. This keeps the GitHub-rendered README useful for users while keeping the documentation site as the canonical public guide surface.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The published documentation source includes a clear link to the Cadder GitHub repository at https://github.com/MrMaxie/Cadder.
- [ ] #2 The root README documentation links point to the published documentation pages under https://maxie.dev/Cadder/ instead of docs/site/src/content/docs source files.
- [ ] #3 README links remain useful when rendered on GitHub and do not require browsing repository source paths to read user docs.
- [ ] #4 Relevant documentation validation is run from docs/site, including at least the existing docs check or build command.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
