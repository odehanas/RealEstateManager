// Build Your Own Report.
//
// The page collects a report definition (source, columns or grouping, filters, sort,
// chart), the server runs it against the company's own data (/ReportBuilder/Run) and
// the result is drawn here as a table and, if chosen, a Chart.js chart.
(function () {
    'use strict';

    var sourceSelect = document.getElementById('rbSource');
    if (!sourceSelect) { return; }

    var $ = function (id) { return document.getElementById(id); };
    var builder = $('rbBuilder');
    var fieldList = $('rbFieldList');
    var fieldSearch = $('rbFieldSearch');
    var chips = $('rbChips');
    var groupBy = $('rbGroupBy');
    var dateGroup = $('rbDateGroup');
    var measure = $('rbMeasure');
    var measureField = $('rbMeasureField');
    var filtersBox = $('rbFilters');
    var sortSelect = $('rbSort');
    var sortDir = $('rbSortDir');
    var limitInput = $('rbLimit');
    var message = $('rbMessage');
    var chartBox = $('rbChartBox');
    var tableBox = $('rbTableBox');
    var table = $('rbTable');
    var foot = $('rbFoot');
    var title = $('rbTitle');

    var fields = [];          // [{ path, label, kind }]
    var byPath = {};
    var columns = [];         // selected column paths, in order
    var chartType = 'table';
    var chart = null;
    var lastResult = null;
    var currentReportId = null;
    var saved = JSON.parse($('rbSaved').textContent || '[]');

    var OPS = {
        text: [['contains', 'contains'], ['eq', 'is'], ['ne', 'is not'], ['startswith', 'starts with'], ['notcontains', 'does not contain'], ['in', 'is one of (a, b, c)'], ['empty', 'is empty'], ['notempty', 'is not empty']],
        number: [['eq', '='], ['ne', '≠'], ['gt', '>'], ['gte', '≥'], ['lt', '<'], ['lte', '≤'], ['between', 'between'], ['empty', 'is empty'], ['notempty', 'is not empty']],
        date: [['eq', 'on'], ['between', 'between'], ['gte', 'on or after'], ['lte', 'on or before'], ['empty', 'is empty'], ['notempty', 'is not empty']],
        bool: [['eq', 'is']]
    };

    function token() {
        var field = document.querySelector('input[name="__RequestVerificationToken"]');
        return field ? field.value : '';
    }

    function request(url, body) {
        var options = { credentials: 'same-origin', headers: { 'RequestVerificationToken': token() } };
        if (body !== undefined) {
            options.method = 'POST';
            options.headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(body);
        }
        return fetch(url, options).then(function (response) {
            return response.json().catch(function () { return {}; }).then(function (data) {
                if (!response.ok) { throw new Error(data.error || ('Error ' + response.status)); }
                return data;
            });
        });
    }

    function el(tag, attrs, text) {
        var node = document.createElement(tag);
        Object.keys(attrs || {}).forEach(function (k) { node.setAttribute(k, attrs[k]); });
        if (text !== undefined) { node.textContent = text; }
        return node;
    }

    function mode() { return document.querySelector('input[name="rbMode"]:checked').value; }

    // ------------------------------------------------------------------ fields

    function groupName(field) {
        var i = field.label.lastIndexOf(' › ');
        return i < 0 ? 'This record' : field.label.substring(0, i);
    }

    function shortLabel(field) {
        var i = field.label.lastIndexOf(' › ');
        return i < 0 ? field.label : field.label.substring(i + 3);
    }

    function fillSelect(select, list, blank) {
        select.innerHTML = '';
        if (blank) { select.appendChild(el('option', { value: '' }, blank)); }
        var groups = {};
        list.forEach(function (f) {
            var g = groupName(f);
            if (!groups[g]) { groups[g] = el('optgroup', { label: g }); select.appendChild(groups[g]); }
            // The full path, so a closed box reads "Unit › Property › Name", not just "Name".
            groups[g].appendChild(el('option', { value: f.path }, f.label));
        });
    }

    function renderFieldList() {
        var q = fieldSearch.value.trim().toLowerCase();
        fieldList.innerHTML = '';
        var last = null;
        fields.filter(function (f) { return !q || f.label.toLowerCase().indexOf(q) >= 0; }).slice(0, 400).forEach(function (f) {
            var g = groupName(f);
            if (g !== last) { fieldList.appendChild(el('div', { 'class': 'rb-group' }, g)); last = g; }
            var label = el('label');
            var box = el('input', { type: 'checkbox', 'class': 'form-check-input me-1' });
            box.checked = columns.indexOf(f.path) >= 0;
            box.addEventListener('change', function () {
                if (box.checked) { if (columns.indexOf(f.path) < 0) { columns.push(f.path); } }
                else { columns = columns.filter(function (c) { return c !== f.path; }); }
                renderChips();
            });
            label.appendChild(box);
            label.appendChild(document.createTextNode(shortLabel(f)));
            fieldList.appendChild(label);
        });
    }

    function renderChips() {
        chips.innerHTML = '';
        columns.forEach(function (path) {
            var f = byPath[path];
            if (!f) { return; }
            var chip = el('span', { 'class': 'rb-chip' }, f.label);
            var x = el('button', { type: 'button', title: 'Remove' }, '×');
            x.addEventListener('click', function () {
                columns = columns.filter(function (c) { return c !== path; });
                renderChips(); renderFieldList();
            });
            chip.appendChild(x);
            chips.appendChild(chip);
        });
    }

    function loadFields(source) {
        return request('/ReportBuilder/Fields?source=' + encodeURIComponent(source)).then(function (list) {
            fields = list;
            byPath = {};
            list.forEach(function (f) { byPath[f.path] = f; });
            fillSelect(groupBy, list, 'Choose a field…');
            fillSelect(measureField, list.filter(function (f) { return f.kind === 'number'; }), 'Choose a number…');
            fillSelect(sortSelect, list, 'No particular order');
            filtersBox.innerHTML = '';
            builder.hidden = false;
        });
    }

    // ------------------------------------------------------------------ filters

    function addFilter(def) {
        var row = el('div', { 'class': 'rb-filter' });
        var field = el('select', { 'class': 'form-select form-select-sm' });
        fillSelect(field, fields);
        var op = el('select', { 'class': 'form-select form-select-sm' });
        var values = el('div', { 'class': 'd-flex gap-1' });
        var v1 = el('input', { 'class': 'form-control form-control-sm', placeholder: 'value' });
        var v2 = el('input', { 'class': 'form-control form-control-sm', placeholder: 'and' });
        var remove = el('button', { type: 'button', 'class': 'btn btn-sm btn-link text-danger p-0', title: 'Remove filter' }, 'Remove');
        values.appendChild(v1); values.appendChild(v2);

        function refresh(keepOp) {
            var f = byPath[field.value] || { kind: 'text' };
            var current = op.value;
            op.innerHTML = '';
            OPS[f.kind].forEach(function (o) { op.appendChild(el('option', { value: o[0] }, o[1])); });
            if (keepOp && OPS[f.kind].some(function (o) { return o[0] === current; })) { op.value = current; }
            var type = f.kind === 'date' ? 'date' : f.kind === 'number' ? 'number' : 'text';
            v1.type = v2.type = type;
            if (f.kind === 'bool') {
                v1.type = 'text'; v1.placeholder = 'yes or no';
            }
            v1.hidden = op.value === 'empty' || op.value === 'notempty';
            v2.hidden = op.value !== 'between';
        }

        field.addEventListener('change', function () { refresh(false); });
        op.addEventListener('change', function () { refresh(true); });
        remove.addEventListener('click', function () { row.remove(); });

        if (def) { field.value = def.field; }
        refresh(false);
        if (def) { op.value = def.op; v1.value = def.value || ''; v2.value = def.value2 || ''; refresh(true); }

        row.appendChild(field); row.appendChild(op); row.appendChild(values); row.appendChild(remove);
        row.read = function () { return { field: field.value, op: op.value, value: v1.value, value2: v2.value }; };
        filtersBox.appendChild(row);
    }

    // ------------------------------------------------------------------ definition

    function definition() {
        var summary = mode() === 'summary';
        return {
            source: sourceSelect.value,
            columns: summary ? [] : columns.slice(),
            filters: Array.prototype.map.call(filtersBox.children, function (r) { return r.read(); }),
            groupBy: summary ? (groupBy.value || null) : null,
            dateGroup: summary ? dateGroup.value : null,
            measure: summary ? measure.value : 'count',
            measureField: summary && measure.value !== 'count' ? measureField.value : null,
            sortBy: summary ? (sortSelect.value === 'value' || sortSelect.value === 'group' ? sortSelect.value : null) : (sortSelect.value || null),
            sortDesc: sortDir.value === 'desc',
            limit: parseInt(limitInput.value, 10) || 500,
            chart: chartType
        };
    }

    function applyDefinition(def) {
        sourceSelect.value = def.source;
        return loadFields(def.source).then(function () {
            var summary = !!def.groupBy || (def.measure && def.measure !== 'count' && (!def.columns || def.columns.length === 0));
            (summary ? $('rbModeSummary') : $('rbModeList')).checked = true;
            columns = (def.columns || []).filter(function (c) { return byPath[c]; });
            groupBy.value = def.groupBy || '';
            dateGroup.value = def.dateGroup || 'month';
            measure.value = def.measure || 'count';
            measureField.value = def.measureField || '';
            (def.filters || []).forEach(addFilter);
            limitInput.value = def.limit || 500;
            sortDir.value = def.sortDesc ? 'desc' : 'asc';
            setChart(def.chart || 'table');
            updateMode();
            if (!summary) { sortSelect.value = def.sortBy || ''; }
            else if (def.sortBy) { sortSelect.value = def.sortBy; }
            renderFieldList(); renderChips();
        });
    }

    function updateMode() {
        var summary = mode() === 'summary';
        $('rbListOptions').hidden = summary;
        $('rbSummaryOptions').hidden = !summary;
        var f = byPath[groupBy.value];
        $('rbDateGroupBox').hidden = !(summary && f && f.kind === 'date');
        measureField.disabled = measure.value === 'count';

        // A grouped report sorts by its groups or its totals.
        if (summary) {
            var previous = sortSelect.value;
            sortSelect.innerHTML = '';
            sortSelect.appendChild(el('option', { value: '' }, 'Automatic'));
            sortSelect.appendChild(el('option', { value: 'group' }, 'By group'));
            sortSelect.appendChild(el('option', { value: 'value' }, 'By total'));
            if (previous === 'group' || previous === 'value') { sortSelect.value = previous; }
        } else if (!sortSelect.querySelector('optgroup')) {
            fillSelect(sortSelect, fields, 'No particular order');
        }
    }

    function setChart(type) {
        chartType = type;
        document.querySelectorAll('#rbChartTypes [data-chart]').forEach(function (b) {
            b.classList.toggle('active', b.getAttribute('data-chart') === type);
        });
    }

    // ------------------------------------------------------------------ results

    function format(value, kind) {
        if (value === null || value === undefined) { return ''; }
        if (kind === 'number' && typeof value === 'number') {
            return value.toLocaleString(undefined, { minimumFractionDigits: Number.isInteger(value) ? 0 : 2, maximumFractionDigits: 2 });
        }
        if (typeof value === 'boolean') { return value ? 'Yes' : 'No'; }
        return String(value);
    }

    function run() {
        if (!sourceSelect.value) { return; }
        message.textContent = 'Running…';
        message.hidden = false;
        request('/ReportBuilder/Run', definition()).then(show).catch(function (e) {
            message.textContent = e.message;
        });
    }

    function show(result) {
        lastResult = result;
        $('rbCsv').disabled = !!result.error || !result.rows || result.rows.length === 0;
        if (result.error) {
            message.textContent = result.error;
            message.hidden = false;
            chartBox.hidden = tableBox.hidden = true;
            foot.textContent = '';
            return;
        }

        title.textContent = ($('rbName').value.trim() || result.title);
        message.hidden = result.rows.length > 0;
        message.textContent = 'No records match.';

        table.innerHTML = '';
        var head = el('thead', { 'class': 'table-light' });
        var hr = el('tr');
        result.columns.forEach(function (c) { hr.appendChild(el('th', { 'class': c.kind === 'number' ? 'text-end' : '' }, c.label)); });
        head.appendChild(hr);
        table.appendChild(head);
        var body = el('tbody');
        result.rows.forEach(function (row) {
            var tr = el('tr');
            row.forEach(function (v, i) {
                tr.appendChild(el('td', { 'class': result.columns[i].kind === 'number' ? 'num' : '' }, format(v, result.columns[i].kind)));
            });
            body.appendChild(tr);
        });
        if (result.grouped && result.total !== null && result.total !== undefined && result.rows.length > 1) {
            var tt = el('tr', { 'class': 'fw-bold' });
            tt.appendChild(el('td', {}, 'Total'));
            tt.appendChild(el('td', { 'class': 'num' }, format(result.total, 'number')));
            body.appendChild(tt);
        }
        table.appendChild(body);
        tableBox.hidden = result.rows.length === 0;

        foot.textContent = result.rows.length + ' row(s)' + (result.truncated ? ' - more records exist; narrow the filters or raise the limit.' : '.');
        drawChart(result);
    }

    function drawChart(result) {
        if (chart) { chart.destroy(); chart = null; }
        chartBox.hidden = true;
        if (chartType === 'table' || result.rows.length === 0 || typeof Chart === 'undefined') { return; }

        var labelIndex = 0;
        var valueIndex = -1;
        if (result.grouped) {
            valueIndex = 1;
        } else {
            labelIndex = result.columns.findIndex(function (c) { return c.kind !== 'number'; });
            valueIndex = result.columns.findIndex(function (c) { return c.kind === 'number'; });
            if (labelIndex < 0) { labelIndex = 0; }
        }
        if (valueIndex < 0) {
            foot.textContent += ' A chart needs a number column, or use "Totals by group".';
            return;
        }

        var rows = result.rows.slice(0, chartType === 'pie' ? 12 : 60);
        var palette = ['#1e6fd9', '#e3a008', '#1f9d55', '#d64545', '#7c4dff', '#0e9aa7', '#f97316', '#64748b', '#be185d', '#65a30d', '#0f2740', '#a16207'];
        chartBox.hidden = false;
        chart = new Chart($('rbChart'), {
            type: chartType,
            data: {
                labels: rows.map(function (r) { return format(r[labelIndex], 'text') || '(blank)'; }),
                datasets: [{
                    label: result.columns[valueIndex].label,
                    data: rows.map(function (r) { return r[valueIndex] || 0; }),
                    backgroundColor: chartType === 'line' ? 'rgba(30,111,217,.15)' : rows.map(function (_, i) { return palette[i % palette.length]; }),
                    borderColor: chartType === 'line' ? '#1e6fd9' : '#ffffff',
                    borderWidth: chartType === 'line' ? 2 : 1,
                    fill: chartType === 'line',
                    tension: .25
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: chartType === 'pie', position: 'right' } },
                scales: chartType === 'pie' ? {} : { y: { beginAtZero: true, ticks: { callback: function (v) { return Number(v).toLocaleString(); } } } }
            }
        });
    }

    function exportCsv() {
        if (!lastResult || !lastResult.rows) { return; }
        var esc = function (v) {
            var s = v === null || v === undefined ? '' : String(v);
            return /[",\r\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
        };
        var lines = [lastResult.columns.map(function (c) { return esc(c.label); }).join(',')];
        lastResult.rows.forEach(function (r) { lines.push(r.map(esc).join(',')); });
        // The byte-order mark makes Excel read Arabic and other characters correctly.
        var blob = new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
        var a = el('a', { href: URL.createObjectURL(blob), download: (title.textContent || 'report').replace(/[^\w\- ]+/g, '') + '.csv' });
        document.body.appendChild(a); a.click(); a.remove();
    }

    // ------------------------------------------------------------------ saving

    function renderSaved() {
        var list = $('rbSavedList');
        list.innerHTML = '';
        if (!saved.length) { list.appendChild(el('div', { 'class': 'text-muted' }, 'None yet.')); return; }
        saved.forEach(function (r) {
            var item = el('div', { 'class': 'list-group-item d-flex justify-content-between align-items-center px-0' });
            var open = el('a', { href: '#' }, r.name);
            open.addEventListener('click', function (e) { e.preventDefault(); openReport(r.id); });
            var left = el('div');
            left.appendChild(open);
            left.appendChild(el('div', { 'class': 'text-muted' }, (r.isShared ? 'Shared' : 'Private') + (r.mine ? '' : ' · by ' + r.owner)));
            item.appendChild(left);
            if (r.mine) {
                var del = el('button', { type: 'button', 'class': 'btn btn-sm btn-link text-danger' }, 'Delete');
                del.addEventListener('click', function () {
                    if (!confirm('Delete the report "' + r.name + '"?')) { return; }
                    request('/ReportBuilder/Delete/' + r.id, {}).then(function (data) {
                        saved = data.saved; renderSaved();
                        if (currentReportId === r.id) { currentReportId = null; }
                    });
                });
                item.appendChild(del);
            }
            list.appendChild(item);
        });
    }

    function save(asNew) {
        var name = $('rbName').value.trim();
        var note = $('rbSaveNote');
        if (!sourceSelect.value) { note.textContent = 'Build a report first.'; return; }
        if (!name) { note.textContent = 'Give the report a name.'; return; }
        request('/ReportBuilder/Save', { id: asNew ? null : currentReportId, name: name, isShared: $('rbShared').checked, definition: definition() })
            .then(function (data) {
                currentReportId = data.id; saved = data.saved; renderSaved();
                note.className = 'small mt-2 text-success'; note.textContent = 'Saved.';
            })
            .catch(function (e) { note.className = 'small mt-2 text-danger'; note.textContent = e.message; });
    }

    function openReport(id) {
        request('/ReportBuilder/Load/' + id).then(function (r) {
            currentReportId = r.mine ? r.id : null;
            $('rbName').value = r.name;
            $('rbShared').checked = r.isShared;
            filtersBox.innerHTML = '';
            return applyDefinition(r.definition).then(run);
        }).catch(function (e) { message.textContent = e.message; message.hidden = false; });
    }

    // ------------------------------------------------------------------ wiring

    sourceSelect.addEventListener('change', function () {
        columns = [];
        currentReportId = null;
        if (!sourceSelect.value) { builder.hidden = true; return; }
        loadFields(sourceSelect.value).then(function () {
            // Start with the record's first few own fields so Run shows something at once.
            columns = fields.filter(function (f) { return f.path.indexOf('.') < 0; }).slice(0, 6).map(function (f) { return f.path; });
            updateMode(); renderFieldList(); renderChips();
        }).catch(function (e) { message.textContent = e.message; });
    });
    document.querySelectorAll('input[name="rbMode"]').forEach(function (r) { r.addEventListener('change', updateMode); });
    groupBy.addEventListener('change', updateMode);
    measure.addEventListener('change', updateMode);
    fieldSearch.addEventListener('input', renderFieldList);
    $('rbAddFilter').addEventListener('click', function () { addFilter(); });
    $('rbChartTypes').addEventListener('click', function (e) {
        var b = e.target.closest('[data-chart]');
        if (!b) { return; }
        setChart(b.getAttribute('data-chart'));
        if (lastResult && !lastResult.error) { drawChart(lastResult); }
    });
    $('rbRun').addEventListener('click', run);
    $('rbCsv').addEventListener('click', exportCsv);
    $('rbSave').addEventListener('click', function () { save(false); });
    $('rbSaveAs').addEventListener('click', function () { save(true); });

    renderSaved();
    if (window.rbOpenId) { openReport(window.rbOpenId); }
})();
