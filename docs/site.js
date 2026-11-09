// MultiRoblox website: navigation, latest-release info and small micro-interactions. No dependencies, no tracking.
(function () {
  var REPO = 'blnkDev/MultiRoblox';
  var DL_PREFIX = 'https://github.com/' + REPO + '/releases/download/';

  // ---- navigation: sticky bar gets a background after scrolling; mobile menu ----
  var nav = document.querySelector('.site-nav');
  var btn = document.querySelector('.menu-btn');
  var links = document.getElementById('navlinks');
  function onScroll() { if (nav) nav.classList.toggle('scrolled', window.scrollY > 8); }
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();
  function setMenu(open) {
    if (!btn || !links) return;
    links.classList.toggle('open', open);
    btn.setAttribute('aria-expanded', String(open));
    btn.textContent = open ? 'Close' : 'Menu';
  }
  if (btn && links) {
    btn.addEventListener('click', function () { setMenu(!links.classList.contains('open')); });
    links.addEventListener('click', function (e) { if (e.target.tagName === 'A') setMenu(false); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') setMenu(false); });
  }

  // ---- theme toggle (the starting theme is set by a tiny script in <head>, which avoids a flash) ----
  var themeBtn = document.querySelector('.theme-btn');
  if (themeBtn) themeBtn.addEventListener('click', function () {
    var next = document.documentElement.dataset.theme === 'light' ? 'dark' : 'light';
    document.documentElement.dataset.theme = next;
    try { localStorage.setItem('theme', next); } catch (e) {}
  });

  // ---- changelog page: replaces the static copy with every release published on GitHub ----
  var relBox = document.querySelector('[data-releases]');
  if (relBox) {
    fetch('https://api.github.com/repos/' + REPO + '/releases?per_page=30', { headers: { Accept: 'application/vnd.github+json' } })
      .then(function (r) { return r.ok ? r.json() : Promise.reject(); })
      .then(function (list) {
        list = list.filter(function (r) { return !r.draft && r.body; });
        if (!list.length) return;
        relBox.textContent = '';
        list.forEach(function (rel, i) {
          var box = document.createElement('div'); box.className = 'release';
          var head = document.createElement('div'); head.className = 'release-head';
          var m = /v\d+\.\d+\.\d+/.exec(rel.name || '') || /v\d+\.\d+\.\d+/.exec(rel.tag_name || '');
          var h = document.createElement('h2'); h.textContent = m ? m[0] : (rel.name || rel.tag_name); head.appendChild(h);
          if (rel.prerelease) head.appendChild(chip('wait', 'Pre-release')); else if (i === 0) head.appendChild(chip('on', 'Latest'));
          var d = new Date(rel.published_at);
          if (!isNaN(d)) { var s = document.createElement('span'); s.className = 'release-date'; s.textContent = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }); head.appendChild(s); }
          if (String(rel.html_url).indexOf('https://github.com/' + REPO + '/releases/') === 0) {   // never trust a foreign host
            var a = document.createElement('a'); a.className = 'release-link'; a.href = rel.html_url; a.textContent = 'View on GitHub'; head.appendChild(a);
          }
          var panel = document.createElement('div'); panel.className = 'panel notes'; renderNotes(panel, String(rel.body));
          box.appendChild(head); box.appendChild(panel); relBox.appendChild(box);
        });
      })
      .catch(function () {});   // anything goes wrong: the static copy stays
  }
  function chip(kind, text) { var s = document.createElement('span'); s.className = 'chip ' + kind; s.textContent = text; return s; }

  // ---- latest release: fills [data-dl], [data-version], [data-ver-num], [data-meta], [data-sha], [data-notes] ----
  var wants = document.querySelectorAll('[data-dl],[data-version],[data-ver-num],[data-meta],[data-sha],[data-notes]');
  if (wants.length) {
    fetch('https://api.github.com/repos/' + REPO + '/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
      .then(function (r) { return r.ok ? r.json() : Promise.reject(); })
      .then(fill)
      .catch(function () {});   // anything goes wrong: the static fallback text and links stay
  }

  function each(sel, fn) { Array.prototype.forEach.call(document.querySelectorAll(sel), fn); }

  function fill(rel) {
    var tag = String(rel.tag_name || '');
    if (!/^v?\d+\.\d+\.\d+$/.test(tag)) return;
    var zip = (rel.assets || []).filter(function (a) { return /^MultiRoblox-v\d+\.\d+\.\d+\.zip$/.test(a.name); })[0];
    var url = zip && String(zip.browser_download_url).indexOf(DL_PREFIX) === 0 ? zip.browser_download_url : null;   // never trust a foreign host
    each('[data-dl]', function (a) { a.textContent = 'Download ' + tag; if (url) a.href = url; });
    each('[data-version]', function (el) { el.textContent = tag; });
    each('[data-ver-num]', function (el) { el.textContent = tag.replace(/^v/, ''); });
    var kb = zip ? Math.round(zip.size / 1024) : 0;
    each('[data-meta]', function (el) {
      el.textContent = 'Windows 10 / 11 · free & open source (MIT) · ' + (kb ? 'a ' + kb + ' KB download' : 'a tiny download') + ', no installer';
    });
    each('[data-date]', function (el) {
      var d = new Date(rel.published_at);
      if (!isNaN(d)) el.textContent = 'released ' + d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    });
    each('[data-sha]', function (el) { if (zip && /^sha256:[0-9a-f]{64}$/.test(String(zip.digest))) el.textContent = zip.digest.slice(7); });
    each('[data-notes]', function (el) { if (rel.body) renderNotes(el, String(rel.body)); });
  }

  // Tiny, safe markdown renderer for release notes: only text nodes are created (no innerHTML).
  function inline(parent, text) {
    text.split('`').forEach(function (part, i) {
      if (!part) return;
      if (i % 2) { var c = document.createElement('code'); c.textContent = part; parent.appendChild(c); }
      else parent.appendChild(document.createTextNode(part.replace(/\*\*/g, '')));
    });
  }
  function renderNotes(root, md) {
    root.textContent = '';
    var ul = null;
    md.split(/\r?\n/).forEach(function (line) {
      var m;
      if ((m = /^#{2,4}\s+(.*)/.exec(line))) { ul = null; var h = document.createElement('h4'); inline(h, m[1]); root.appendChild(h); }
      else if ((m = /^[-*]\s+(.*)/.exec(line))) {
        if (!ul) { ul = document.createElement('ul'); root.appendChild(ul); }
        var li = document.createElement('li'); inline(li, m[1]); ul.appendChild(li);
      } else if (line.trim()) { ul = null; var p = document.createElement('p'); inline(p, line.replace(/^>\s?/, '')); root.appendChild(p); }
    });
  }

  // ---- micro-interactions (skipped for reduced motion) ----
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches || !('IntersectionObserver' in window)) return;

  var items = document.querySelectorAll('.page-head h1, .page-head p, section h2, .page-body h2, .sub, .card, .panel, details, .notice, .cta-band, .tablewrap, ol.guide > li');
  Array.prototype.forEach.call(items, function (el, i) { el.classList.add('reveal'); el.style.setProperty('--i', i % 3); });   // light stagger inside a row
  var fired = false;
  var io = new IntersectionObserver(function (entries) {
    fired = true;
    entries.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); } });
  }, { threshold: 0.12 });
  Array.prototype.forEach.call(items, function (el) { io.observe(el); });
  // Failsafe: content must never stay hidden. If the observer has not reported anything after 2.5 s, show everything.
  setTimeout(function () { if (!fired) Array.prototype.forEach.call(items, function (el) { el.classList.add('in'); }); }, 2500);

  if (!window.matchMedia('(hover: hover)').matches) return;
  document.addEventListener('pointermove', function (e) {   // spotlight that follows the pointer inside cards
    var c = e.target.closest && e.target.closest('.card');
    if (!c) return;
    var r = c.getBoundingClientRect();
    c.style.setProperty('--mx', (e.clientX - r.left) + 'px');
    c.style.setProperty('--my', (e.clientY - r.top) + 'px');
  });
  var hero = document.querySelector('.hero'), shot = document.querySelector('.shot img');
  if (hero && shot) {   // gentle tilt of the screenshot on the home page
    hero.addEventListener('pointermove', function (e) {
      var r = shot.getBoundingClientRect();
      var dx = (e.clientX - (r.left + r.width / 2)) / window.innerWidth;
      var dy = (e.clientY - (r.top + r.height / 2)) / window.innerHeight;
      shot.style.transform = 'perspective(900px) rotateY(' + (dx * 10).toFixed(2) + 'deg) rotateX(' + (-dy * 8).toFixed(2) + 'deg)';
    });
    hero.addEventListener('pointerleave', function () { shot.style.transform = ''; });
  }
})();
