import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

export default defineConfig({
  site: 'https://maxie.dev',
  base: '/Cadder',
  integrations: [
    starlight({
      title: 'Cadder',
      description:
        'Documentation for the Cadder cross-platform Rust Caddy coordinator.',
      favicon: 'favicon.ico',
      logo: {
        src: './src/assets/logo.png',
        alt: 'Cadder logo',
      },
      customCss: ['./src/styles/cadder.css'],
      sidebar: [
        {
          label: 'Quick Start',
          items: [
            { label: 'Overview', slug: 'index' },
            { label: 'Getting started', slug: 'guides/getting-started' },
          ],
        },
        {
          label: 'User guide',
          items: [
            { label: 'How to use', slug: 'guides/how-to-use' },
            { label: 'cadder.toml', slug: 'guides/cadder-toml' },
            { label: 'PATH and shim strategy', slug: 'guides/path-and-shim' },
            { label: 'TUI and diagnostics', slug: 'guides/tui-diagnostics' },
          ],
        },
        {
          label: 'Cookbooks',
          items: [
            { label: 'Windows', slug: 'guides/windows' },
            { label: 'macOS', slug: 'guides/macos' },
            { label: 'Linux', slug: 'guides/linux' },
          ],
        },
        {
          label: 'Reference',
          items: [
            { label: 'Runtime and configuration', slug: 'reference/runtime-configuration' },
            { label: 'Real Caddy resolution', slug: 'reference/real-caddy-resolution' },
          ],
        },
      ],
    }),
  ],
});
