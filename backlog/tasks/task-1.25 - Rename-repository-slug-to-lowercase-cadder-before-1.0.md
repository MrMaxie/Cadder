---
id: TASK-1.25
title: Rename repository slug to lowercase cadder before 1.0
status: To Do
assignee: []
created_date: '2026-06-11 16:02'
labels:
  - repository
  - docs
  - release
  - pre-1.0
milestone: v1.0
dependencies: []
references:
  - Cargo.toml
  - .github/workflows/ci.yml
  - .github/workflows/release.yml
  - .github/ISSUE_TEMPLATE/config.yml
  - docs/site/public/cadder-downloads.js
documentation:
  - README.md
  - docs/site/astro.config.mjs
  - docs/site/README.md
  - docs/site/src/content/docs/index.mdx
parent_task_id: TASK-1
priority: medium
ordinal: 25000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Prepare and execute the repository slug rename from `MrMaxie/Cadder` to `MrMaxie/cadder` so the public documentation path can become `https://maxie.dev/cadder/`. Keep `Cadder` as the product name unless a separate branding decision changes it. Minimize disruption to GitHub links, releases, docs publishing, local clone remotes, and CI/CD.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Audit affected repository and docs URLs, including README links, Cargo metadata, docs base path, release links, download scripts, issue/security links, and workflows.
- [ ] #2 Record a low-risk rename plan with required GitHub permissions, change ordering, expected old-URL redirects, and rollback or remediation notes.
- [ ] #3 Rename the GitHub repository slug to lowercase `cadder` and update project-owned repository references to `MrMaxie/cadder` or the matching SSH remote form.
- [ ] #4 Publish docs under `https://maxie.dev/cadder/`; update project-owned `/Cadder` references or document any compatibility leftovers.
- [ ] #5 Verify CI, docs publishing, releases, and release download links still work after the rename.
- [ ] #6 Run targeted searches for `MrMaxie/Cadder`, `maxie.dev/Cadder`, and `/Cadder`; leave only documented compatibility references.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
