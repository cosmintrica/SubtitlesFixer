window.SubtitlesFixerConfig = {
  productName: "Subtitles Fixer",
  version: "1.0.7",
  producer: "Cosmin Trica",
  copyright: "Copyright \u00A9 2026 Cosmin Trica. All rights reserved.",
  downloadUrl: "https://github.com/cosmintrica/SubtitlesFixer/releases/latest",
  donateUrl: "#", // Handled via dropdown
  revolutUrl: "https://revolut.me/mtvtrk",
  stripeUrl: "https://donate.stripe.com/eVq8wI9m9aOTcP88fv3VC01",
  linkedInUrl: "https://www.linkedin.com/in/cosmintrica/",
  githubUrl: "https://github.com/cosmintrica/SubtitlesFixer"
};

(function () {
  const config = window.SubtitlesFixerConfig;

  // Download buttons
  document.querySelectorAll("#download-button, #download-button-bottom, #nav-download").forEach(el => {
    if (el) el.href = config.downloadUrl;
  });

  // Donate dropdown toggle
  document.querySelectorAll(".donate-container").forEach(container => {
    const btn = container.querySelector("button");
    if (btn) {
      btn.addEventListener("click", (e) => {
        e.stopPropagation();
        
        // Close others
        document.querySelectorAll(".donate-container").forEach(c => {
          if (c !== container) c.classList.remove("active");
        });
        
        container.classList.toggle("active");
      });
    }
  });

  // Close dropdown on click outside
  document.addEventListener("click", () => {
    document.querySelectorAll(".donate-container").forEach(c => c.classList.remove("active"));
  });

  // Version text in hero button
  const heroVersion = document.getElementById("hero-version-text");
  if (heroVersion) heroVersion.textContent = `v${config.version}`;
  const bottomVersion = document.getElementById("download-version-bottom");
  if (bottomVersion) bottomVersion.textContent = `v${config.version}`;

  // Footer
  const footer = document.getElementById("footer-copy");
  if (footer) footer.textContent = config.copyright;

  updateDownloadCounts();

  async function updateDownloadCounts() {
    try {
      const response = await fetch("https://api.github.com/repos/cosmintrica/SubtitlesFixer/releases", {
        headers: {
          "Accept": "application/vnd.github+json"
        }
      });

      if (!response.ok) {
        return;
      }

      const releases = await response.json();
      const totalDownloads = Array.isArray(releases)
        ? releases.reduce((sum, release) => {
            const releaseDownloads = Array.isArray(release.assets)
              ? release.assets.reduce((assetSum, asset) => assetSum + (asset.download_count || 0), 0)
              : 0;
            return sum + releaseDownloads;
          }, 0)
        : 0;

      if (totalDownloads <= 0) {
        return;
      }

      const formattedCount = new Intl.NumberFormat("ro-RO").format(totalDownloads);
      const label = `${formattedCount} descărcări pe GitHub`;
      document.querySelectorAll("#download-count-top, #download-count-bottom").forEach(el => {
        if (!el) return;
        el.textContent = label;
        el.hidden = false;
      });
    } catch {
      // Daca GitHub nu raspunde sau limita API este depasita, lasam butonul curat fara count.
    }
  }
})();
