// Click any help screenshot to see it full size. Shared by the staff pages, the
// tenant portal and the full manual. Click anywhere or press Escape to close.
(function () {
    function close() {
        var open = document.querySelector('.shot-lightbox');
        if (open) { open.remove(); }
    }

    document.addEventListener('click', function (event) {
        var img = event.target.closest('[data-shot-zoom]');

        if (img) {
            event.preventDefault();
            close();
            var box = document.createElement('div');
            box.className = 'shot-lightbox';
            box.setAttribute('role', 'dialog');
            box.setAttribute('aria-label', img.getAttribute('alt') || 'Screenshot');
            var big = document.createElement('img');
            big.src = img.getAttribute('data-shot-zoom');
            big.alt = img.getAttribute('alt') || '';
            box.appendChild(big);
            document.body.appendChild(box);
            return;
        }

        if (event.target.closest('.shot-lightbox')) { close(); }
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') { close(); }
    });
})();
