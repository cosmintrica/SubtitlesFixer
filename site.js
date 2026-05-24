window.SubtitlesFixerConfig = {
  productName: "Subtitles Fixer",
  version: "1.0.9",
  producer: "Cosmin Trica",
  copyright: "Copyright \u00A9 2026 Cosmin Trica. All rights reserved.",
  downloadUrl: "https://github.com/cosmintrica/SubtitlesFixer/releases/latest",
  donateUrl: "#",
  revolutUrl: "https://revolut.me/mtvtrk",
  stripeUrl: "https://donate.stripe.com/eVq8wI9m9aOTcP88fv3VC01",
  linkedInUrl: "https://www.linkedin.com/in/cosmintrica/",
  githubUrl: "https://github.com/cosmintrica/SubtitlesFixer"
};

(function () {
  const config = window.SubtitlesFixerConfig;
  const i18n = window.SubtitlesFixerI18n;
  let lastDownloadTotal = 0;

  document.querySelectorAll("#download-button, #download-button-bottom, #nav-download").forEach(el => {
    if (el) el.href = config.downloadUrl;
  });

  const donateContainers = document.querySelectorAll(".donate-container");
  const donateBackdrop = document.getElementById("donate-backdrop");
  const mobileDonateMq = window.matchMedia("(max-width: 720px)");

  function usesFixedDonateMenu() {
    return mobileDonateMq.matches;
  }

  function positionDonateMenu(container) {
    const menu = container.querySelector(".donate-menu");
    const button = container.querySelector("button");
    if (!menu || !button) return;

    if (!usesFixedDonateMenu()) {
      menu.style.removeProperty("--donate-menu-top");
      menu.style.removeProperty("--donate-menu-left");
      return;
    }

    const rect = button.getBoundingClientRect();
    menu.style.setProperty("--donate-menu-top", `${rect.bottom + 12}px`);
    menu.style.setProperty("--donate-menu-left", `${rect.left + rect.width / 2}px`);
  }

  function syncDonateBackdrop() {
    if (!donateBackdrop) return;
    const open = Array.from(donateContainers).some(c => c.classList.contains("active"));
    const showBackdrop = open && usesFixedDonateMenu();
    donateBackdrop.classList.toggle("active", showBackdrop);
    donateBackdrop.setAttribute("aria-hidden", showBackdrop ? "false" : "true");
  }

  function clearDonateMenuPosition(container) {
    const menu = container.querySelector(".donate-menu");
    menu?.style.removeProperty("--donate-menu-top");
    menu?.style.removeProperty("--donate-menu-left");
  }

  function setDonateOpen(container, open) {
    container.classList.toggle("active", open);
    const button = container.querySelector("button");
    if (button) button.setAttribute("aria-expanded", open ? "true" : "false");

    if (open) {
      requestAnimationFrame(() => positionDonateMenu(container));
    } else {
      clearDonateMenuPosition(container);
    }

    syncDonateBackdrop();
  }

  function closeDonateMenus(except = null) {
    donateContainers.forEach(container => {
      if (container !== except) setDonateOpen(container, false);
    });
    syncDonateBackdrop();
  }

  function repositionOpenDonateMenus() {
    donateContainers.forEach(container => {
      if (container.classList.contains("active")) positionDonateMenu(container);
    });
  }

  if (donateBackdrop) {
    donateBackdrop.addEventListener("click", () => closeDonateMenus());
  }

  mobileDonateMq.addEventListener("change", () => {
    donateContainers.forEach(clearDonateMenuPosition);
    repositionOpenDonateMenus();
  });

  window.addEventListener("resize", repositionOpenDonateMenus);
  window.addEventListener("scroll", repositionOpenDonateMenus, { passive: true });

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

  document.addEventListener("click", () => closeDonateMenus());
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeDonateMenus();
  });

  const heroVersion = document.getElementById("hero-version-text");
  if (heroVersion) heroVersion.textContent = `v${config.version}`;
  const bottomVersion = document.getElementById("download-version-bottom");
  if (bottomVersion) bottomVersion.textContent = `v${config.version}`;

  const footer = document.getElementById("footer-copy");
  if (footer) footer.textContent = config.copyright;

  function updateDownloadCountLabels() {
    if (lastDownloadTotal <= 0 || !i18n) return;

    const lang = document.documentElement.lang === "en" ? "en" : "ro";
    const template = i18n.getNested(i18n[lang], "downloads.count");
    const locale = lang === "ro" ? "ro-RO" : "en-US";
    const formattedCount = new Intl.NumberFormat(locale).format(lastDownloadTotal);
    const label = i18n.format(template, { count: formattedCount });

    document.querySelectorAll("#download-count-top, #download-count-bottom").forEach(el => {
      if (!el) return;
      el.textContent = label;
      el.hidden = false;
    });
  }

  if (i18n) {
    i18n.init(config.version);
    window.addEventListener("sf-lang-change", updateDownloadCountLabels);
  }

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

      lastDownloadTotal = totalDownloads;
      updateDownloadCountLabels();
    } catch {
      // If GitHub is unavailable or rate-limited, keep the button without a count.
    }
  }
})();
