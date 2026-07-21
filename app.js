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

const formatNumber = (value) => new Intl.NumberFormat('en-US').format(value);

async function loadReleaseStats() {
  const downloadCount = document.querySelector('#download-count');
  const releaseCount = document.querySelector('#release-count');
  const releaseVersion = document.querySelector('#release-version');

  try {
    const response = await fetch('https://api.github.com/repos/fastnick21/Roletopia/releases?per_page=100', {
      headers: { Accept: 'application/vnd.github+json' }
    });

    if (!response.ok) {
      throw new Error(`GitHub request failed with ${response.status}`);
    }

    const releases = await response.json();
    const totalDownloads = releases.reduce((releaseTotal, release) => {
      return releaseTotal + release.assets.reduce((assetTotal, asset) => {
        return assetTotal + (asset.download_count || 0);
      }, 0);
    }, 0);

    downloadCount.textContent = formatNumber(totalDownloads);
    releaseCount.textContent = formatNumber(releases.length);

    if (releases.length > 0) {
      releaseVersion.textContent = `${releases[0].tag_name || releases[0].name || 'Latest'} for Windows`;
    } else {
      releaseVersion.textContent = 'First release coming soon';
    }
  } catch (error) {
    console.error('Unable to load GitHub release statistics:', error);
    downloadCount.textContent = 'Unavailable';
    releaseCount.textContent = 'Unavailable';
    releaseVersion.textContent = 'View releases on GitHub';
  }
}

loadReleaseStats();

const sections = [...document.querySelectorAll('main section[id]')];
const navLinks = [...document.querySelectorAll('.main-nav a[href^="#"]')];

const observer = new IntersectionObserver((entries) => {
  entries.forEach((entry) => {
    if (!entry.isIntersecting) return;
    navLinks.forEach((link) => {
      link.classList.toggle('active', link.getAttribute('href') === `#${entry.target.id}`);
    });
  });
}, { rootMargin: '-35% 0px -55% 0px' });

sections.forEach((section) => observer.observe(section));
