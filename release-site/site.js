window.SubtitlesFixerConfig = {
  productName: "Subtitles Fixer",
  version: "1.0.0",
  producer: "Cosmin Trica",
  copyright: "Copyright \u00A9 2026 Cosmin Trica. All rights reserved.",
  downloadUrl: "https://github.com/cosmintrica/SubtitlesFixer/releases/latest",
  donateUrl: "#", // Handled via dropdown
  revolutUrl: "https://revolut.me/mtvtrk",
  stripeUrl: "https://donate.stripe.com/cNi4gs9dB8wv4GXa6Vbsc00",
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

  // Footer
  const footer = document.getElementById("footer-copy");
  if (footer) footer.textContent = config.copyright;

  // Add click tracking or logging if needed (optional)
})();
