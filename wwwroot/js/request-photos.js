// Photos on a tenant's request. Phone cameras make pictures of several megabytes; before
// they are sent, each one is made smaller here (longest side 1600 px, JPEG), so an upload
// on mobile data takes seconds and the limit on file size is rarely met. A browser that
// cannot do this sends the files as they are, and the server applies its own limits.
(function () {
    var LONGEST_SIDE = 1600;
    var QUALITY = 0.82;

    function shrink(file) {
        return new Promise(function (resolve) {
            if (!/^image\/(jpeg|png|webp)$/.test(file.type) || !window.createImageBitmap) return resolve(file);
            createImageBitmap(file).then(function (bitmap) {
                var scale = Math.min(1, LONGEST_SIDE / Math.max(bitmap.width, bitmap.height));
                // Small already: leave it exactly as taken.
                if (scale === 1 && file.size < 600 * 1024) return resolve(file);
                var canvas = document.createElement('canvas');
                canvas.width = Math.round(bitmap.width * scale);
                canvas.height = Math.round(bitmap.height * scale);
                canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height);
                canvas.toBlob(function (blob) {
                    if (!blob || blob.size >= file.size) return resolve(file);
                    var name = file.name.replace(/\.[^.]+$/, '') + '.jpg';
                    resolve(new File([blob], name, { type: 'image/jpeg' }));
                }, 'image/jpeg', QUALITY);
            }).catch(function () { resolve(file); });
        });
    }

    document.querySelectorAll('input.request-photos').forEach(function (input) {
        var max = parseInt(input.getAttribute('data-max') || '4', 10);
        var note = input.closest('form').querySelector('.request-photos-note');
        var tooMany = input.getAttribute('data-too-many') || '';

        input.addEventListener('change', function () {
            var files = Array.prototype.slice.call(input.files || []);
            if (note) note.textContent = files.length > max ? tooMany : '';
            files = files.slice(0, max);
            if (!window.DataTransfer) return;

            // Hold the form, not the field: a disabled field would not be sent at all.
            var buttons = input.form.querySelectorAll('button[type="submit"]');
            buttons.forEach(function (b) { b.disabled = true; });
            Promise.all(files.map(shrink)).then(function (small) {
                var transfer = new DataTransfer();
                small.forEach(function (f) { transfer.items.add(f); });
                input.files = transfer.files;
            }).catch(function () { /* send them as chosen */ }).then(function () {
                buttons.forEach(function (b) { b.disabled = false; });
            });
        });
    });
})();
