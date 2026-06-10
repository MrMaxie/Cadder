import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

export default defineConfig({
  integrations: [
    starlight({
      title: 'Cadder',
      description:
        'Documentation for the Cadder cross-platform Rust Caddy coordinator.',
      favicon: '/favicon.svg',
      sidebar: [
        {
          label: 'Start here',
          items: [
            { label: 'Overview', slug: 'index' },
            { label: 'Getting started', slug: 'guides/getting-started' },
            { label: 'Validation', slug: 'guides/validation' },
          ],
        },
        {
          label: 'Concepts',
          items: [
            { label: 'Architecture', slug: 'reference/architecture' },
            { label: 'Portable binaries', slug: 'guides/portable-binaries' },
            { label: 'Real Caddy resolution', slug: 'reference/real-caddy-resolution' },
            { label: 'Runtime and configuration', slug: 'reference/runtime-configuration' },
          ],
        },
        {
          label: 'Usage',
          items: [
            { label: 'PATH and shim strategy', slug: 'guides/path-and-shim' },
            { label: 'Windows', slug: 'guides/windows' },
            { label: 'macOS', slug: 'guides/macos' },
            { label: 'Linux', slug: 'guides/linux' },
            { label: 'TUI and diagnostics', slug: 'guides/tui-diagnostics' },
          ],
        },
        {
          label: 'Release',
          items: [{ label: 'Release process', slug: 'guides/release-process' }],
        },
      ],
    }),
  ],
});
