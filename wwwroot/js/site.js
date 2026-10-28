// The side menu (Views/Shared/_Layout.cshtml): showing and hiding it, and opening the
// section of the page being shown.
(function () {
    'use strict';

    var sidebar = document.getElementById('appSidebar');
    if (!sidebar) return;

    var body = document.body;
    var toggle = document.querySelector('[data-sidebar-toggle]');
    var wide = window.matchMedia('(min-width: 992px)');
    var topbar = document.querySelector('.app-topbar');

    function remember(hidden) {
        try { localStorage.setItem('rem-sidebar', hidden ? 'hidden' : 'shown'); } catch (e) { /* private mode */ }
    }

    function isShown() {
        return wide.matches ? !body.classList.contains('sidebar-hidden') : body.classList.contains('sidebar-open');
    }

    // A hidden menu is out of the keyboard's way as well as out of sight.
    function sync() {
        if (toggle) toggle.setAttribute('aria-expanded', isShown() ? 'true' : 'false');
        if (isShown()) sidebar.removeAttribute('inert'); else sidebar.setAttribute('inert', '');
    }

    // The top bar can wrap onto two lines on a narrow screen; the menu starts below it.
    function placeBelowTopbar() {
        if (topbar) document.documentElement.style.setProperty('--topbar-h', topbar.offsetHeight + 'px');
    }

    if (toggle) {
        toggle.addEventListener('click', function () {
            if (wide.matches) {
                remember(body.classList.toggle('sidebar-hidden'));
            } else {
                body.classList.toggle('sidebar-open');
            }
            sync();
        });
    }

    document.querySelectorAll('[data-sidebar-close]').forEach(function (el) {
        el.addEventListener('click', function () { body.classList.remove('sidebar-open'); sync(); });
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && body.classList.contains('sidebar-open')) {
            body.classList.remove('sidebar-open');
            sync();
            if (toggle) toggle.focus();
        }
    });

    wide.addEventListener('change', function () { body.classList.remove('sidebar-open'); sync(); placeBelowTopbar(); });
    window.addEventListener('resize', placeBelowTopbar);

    // The page being shown: its link is marked and its section opened. The longest match
    // wins, so /Journal/GeneralLedger is not taken for /Journal.
    function clean(path) {
        var p = path.replace(/\/+$/, '').toLowerCase();
        return p.replace(/\/index$/, '') || '/';
    }

    var here = clean(location.pathname);
    var best = null, bestLength = -1;
    sidebar.querySelectorAll('a.sidebar-link[href]').forEach(function (link) {
        var path = clean(new URL(link.href, location.origin).pathname);
        var matches = path === '/' ? here === '/' : (here === path || here.indexOf(path + '/') === 0);
        if (matches && path.length > bestLength) { best = link; bestLength = path.length; }
    });

    if (best) {
        best.classList.add('active');
        best.setAttribute('aria-current', 'page');
        var section = best.closest('.sidebar-sub');
        if (section) {
            section.classList.add('show');
            var button = sidebar.querySelector('[data-bs-target="#' + section.id + '"]');
            if (button) { button.classList.remove('collapsed'); button.setAttribute('aria-expanded', 'true'); }
        }
        // Keep the marked link in view in a long menu.
        if (best.getBoundingClientRect().bottom > window.innerHeight - 40) best.scrollIntoView({ block: 'center' });
    }

    placeBelowTopbar();
    sync();
})();
