const menuButton = document.querySelector('.menu-button');
const mainNav = document.querySelector('.main-nav');

if (menuButton && mainNav) {
  menuButton.addEventListener('click', () => {
    const isOpen = mainNav.classList.toggle('open');
    menuButton.setAttribute('aria-expanded', String(isOpen));
  });

  mainNav.querySelectorAll('a').forEach((link) => {
    link.addEventListener('click', () => {
      mainNav.classList.remove('open');
      menuButton.setAttribute('aria-expanded', 'false');
    });
  });
}

const currentPage = document.body.dataset.page;
if (currentPage) {
  document.querySelectorAll(`[data-nav="${currentPage}"]`).forEach((link) => link.classList.add('active'));
}

const formatNumber = (value) => new Intl.NumberFormat('en-US').format(value);

async function loadReleaseData() {
  const downloadTargets = document.querySelectorAll('#download-count, [data-download-count]');
  const releaseTargets = document.querySelectorAll('#release-count, [data-release-count]');
  const versionTargets = document.querySelectorAll('#release-version, [data-release-version]');
  const releaseLinks = document.querySelectorAll('[data-latest-release-link]');

  if (!downloadTargets.length && !releaseTargets.length && !versionTargets.length && !releaseLinks.length) return;

  try {
    const response = await fetch('https://api.github.com/repos/fastnick21/Roletopia/releases?per_page=100', {
      headers: { Accept: 'application/vnd.github+json' }
    });
    if (!response.ok) throw new Error(`GitHub request failed with ${response.status}`);

    const releases = await response.json();
    const totalDownloads = releases.reduce((sum, release) => {
      return sum + (release.assets || []).reduce((assetSum, asset) => assetSum + (asset.download_count || 0), 0);
    }, 0);
    const latest = releases[0];

    downloadTargets.forEach((node) => { node.textContent = formatNumber(totalDownloads); });
    releaseTargets.forEach((node) => { node.textContent = formatNumber(releases.length); });

    if (latest) {
      const versionName = latest.tag_name || latest.name || 'Latest release';
      versionTargets.forEach((node) => { node.textContent = `${versionName} for Windows`; });
      releaseLinks.forEach((link) => {
        link.href = latest.html_url || 'https://github.com/fastnick21/Roletopia/releases/latest';
        link.removeAttribute('aria-disabled');
      });
    } else {
      versionTargets.forEach((node) => { node.textContent = 'Playable release coming soon'; });
      releaseLinks.forEach((link) => link.setAttribute('aria-disabled', 'true'));
    }
  } catch (error) {
    console.error('Unable to load GitHub release information:', error);
    downloadTargets.forEach((node) => { node.textContent = '—'; });
    releaseTargets.forEach((node) => { node.textContent = '—'; });
    versionTargets.forEach((node) => { node.textContent = 'View development on GitHub'; });
  }
}

loadReleaseData();

const revealObserver = 'IntersectionObserver' in window
  ? new IntersectionObserver((entries, observer) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('visible');
        observer.unobserve(entry.target);
      });
    }, { threshold: 0.12 })
  : null;

document.querySelectorAll('.reveal').forEach((element) => {
  if (revealObserver) revealObserver.observe(element);
  else element.classList.add('visible');
});

const roleButtons = document.querySelectorAll('[data-role-filter]');
const roleCards = document.querySelectorAll('[data-role-alignment]');
if (roleButtons.length && roleCards.length) {
  roleButtons.forEach((button) => {
    button.addEventListener('click', () => {
      const filter = button.dataset.roleFilter;
      roleButtons.forEach((item) => item.classList.toggle('active', item === button));
      roleCards.forEach((card) => {
        card.hidden = filter !== 'all' && card.dataset.roleAlignment !== filter;
      });
    });
  });
}

document.querySelectorAll('.faq details').forEach((details) => {
  details.addEventListener('toggle', () => {
    const marker = details.querySelector('summary span');
    if (marker) marker.textContent = details.open ? '−' : '+';
  });
});
