window.SubtitlesFixerConfig = {
  productName: "Subtitles Fixer",
  version: "1.0.9",
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

  const donateContainers = document.querySelectorAll(".donate-container");

  function setDonateOpen(container, open) {
    container.classList.toggle("active", open);
    const section = container.closest(".hero, .cta-bottom");
    if (section) section.classList.toggle("donate-open", open);
    const button = container.querySelector("button");
    if (button) button.setAttribute("aria-expanded", open ? "true" : "false");
  }

  function closeDonateMenus(except = null) {
    donateContainers.forEach(container => {
      if (container !== except) setDonateOpen(container, false);
    });
  }

  // Donate dropdown toggle
  donateContainers.forEach(container => {
    const btn = container.querySelector("button");
    const menu = container.querySelector(".donate-menu");

    if (menu) {
      menu.addEventListener("click", e => e.stopPropagation());
    }

    if (btn) {
      btn.addEventListener("click", (e) => {
        e.stopPropagation();
        const shouldOpen = !container.classList.contains("active");
        closeDonateMenus(container);
        setDonateOpen(container, shouldOpen);
      });
    }
  });

  // Close dropdown on click outside or Escape
  document.addEventListener("click", () => closeDonateMenus());
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeDonateMenus();
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
