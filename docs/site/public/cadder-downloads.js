const cadderAssetPatterns = {
  'windows-x64': /^cadder-.+-windows-x64\.zip$/,
  'macos-arm64': /^cadder-.+-macos-arm64\.tar\.gz$/,
  'macos-x64': /^cadder-.+-macos-x64\.tar\.gz$/,
  'linux-x64': /^cadder-.+-linux-x64\.tar\.gz$/,
};

async function updateCadderDownloadLinks() {
  try {
    const response = await fetch('https://api.github.com/repos/MrMaxie/Cadder/releases?per_page=1');
    if (!response.ok) return;

    const releases = await response.json();
    const release = Array.isArray(releases) ? releases[0] : null;
    if (!release?.assets) return;

    document.querySelectorAll('[data-cadder-asset]').forEach((link) => {
      const key = link.getAttribute('data-cadder-asset');
      const pattern = cadderAssetPatterns[key];
      const asset = release.assets.find((candidate) => pattern?.test(candidate.name));
      if (asset?.browser_download_url) {
        link.href = asset.browser_download_url;
      }
    });
  } catch {
    return;
  }
}

updateCadderDownloadLinks();
