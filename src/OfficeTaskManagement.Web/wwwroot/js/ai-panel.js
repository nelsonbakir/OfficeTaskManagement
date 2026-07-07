/**
 * AI Estimation Panel — OfficeTaskManagement
 * Handles: trigger detection, API calls, form field population, child creation
 * Spec: ai-agent-plan/07_FRONTEND_UX.md
 */
(function () {
    'use strict';

    const panel = document.getElementById('ai-panel');
    if (!panel) return;

    const entityType  = panel.dataset.entityType;
    const childType   = panel.dataset.childType || null;
    const entityId    = panel.dataset.entityId || null;

    // Store the last estimation for retry
    let _lastAction = null;

    // ── Trigger: enable Analyze button when title has ≥10 chars ──────────────
    const titleInput = document.querySelector(
        '[name="Epic.Name"], [name="Feature.Name"], [name="UserStory.Title"], ' +
        '[name="TaskItem.Title"], [name="Project.Name"]'
    );
    const analyzeBtn = document.getElementById('ai-analyze-btn');

    if (titleInput && analyzeBtn) {
        titleInput.addEventListener('input', debounce(() => {
            analyzeBtn.disabled = titleInput.value.trim().length < 10;
        }, 300));
    }

    // ── Analyze button click ──────────────────────────────────────────────────
    analyzeBtn?.addEventListener('click', async () => {
        const title = titleInput?.value.trim() ?? '';
        const description = getDescriptionValue();
        _lastAction = () => doAnalyze(title, description);
        await doAnalyze(title, description);
    });

    async function doAnalyze(title, description) {
        showLoading();
        try {
            const [estimation, children] = await Promise.all([
                fetchEstimation(title, description),
                childType ? fetchChildren(title, description, 'step') : Promise.resolve(null)
            ]);
            renderEstimation(estimation);
            if (children) renderChildren(children);
        } catch (err) {
            showError(err.message || 'AI service unavailable. You can continue without AI estimates.');
            if (typeof window.loadFailedJobs === 'function') {
                window.loadFailedJobs();
            }
        }
    }

    // ── Re-estimate button ────────────────────────────────────────────────────
    document.getElementById('ai-reestimate-btn')?.addEventListener('click', async () => {
        const title = titleInput?.value.trim() ?? '';
        const originalHours = parseFloat(
            document.querySelector('[name="TaskItem.EstimatedHours"]')?.value ?? '0'
        ) || 0;
        _lastAction = () => doReEstimate(title, originalHours);
        await doReEstimate(title, originalHours);
    });

    async function doReEstimate(title, originalHours) {
        showLoading();
        try {
            const resp = await fetch('/api/ai/reestimate', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getAntiForgeryToken()
                },
                body: JSON.stringify({
                    entityType,
                    entityId: parseInt(entityId ?? '0'),
                    title,
                    originalEstimatedHours: originalHours
                })
            });
            if (!resp.ok) {
                const text = await resp.text();
                throw new Error(text || 'Re-estimation failed.');
            }
            const result = await resp.json();
            renderEstimation(result);
        } catch (err) {
            showError('Re-estimation failed. ' + err.message);
            if (typeof window.loadFailedJobs === 'function') {
                window.loadFailedJobs();
            }
        }
    }

    // ── Retry button ──────────────────────────────────────────────────────────
    document.getElementById('ai-retry-btn')?.addEventListener('click', () => {
        if (_lastAction) _lastAction();
    });

    // ── Depth mode switch (step vs full) ─────────────────────────────────────
    document.querySelectorAll('[name="ai-depth"]').forEach(radio => {
        radio.addEventListener('change', async () => {
            if (radio.value === 'full') {
                const title = titleInput?.value.trim() ?? '';
                _lastAction = () => doFullCascade(title);
                await doFullCascade(title);
            } else {
                // Switch back to step: re-fetch step children
                const title = titleInput?.value.trim() ?? '';
                const description = getDescriptionValue();
                showLoading();
                try {
                    const children = await fetchChildren(title, description, 'step');
                    renderChildren(children);
                } catch (err) {
                    showError('Could not load suggestions. ' + err.message);
                }
            }
        });
    });

    async function doFullCascade(title) {
        showLoading();
        try {
            const result = await fetch('/api/ai/full-cascade', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getAntiForgeryToken()
                },
                body: JSON.stringify({
                    parentType: entityType,
                    parentId: parseInt(entityId ?? '0'),
                    parentTitle: title,
                    projectId: panel.dataset.projectId ? parseInt(panel.dataset.projectId) : null
                })
            }).then(r => { if (!r.ok) throw new Error('Cascade failed.'); return r.json(); });
            renderFullCascade(result);
        } catch (err) {
            showError('Full cascade failed. ' + err.message);
        }
    }

    // ── Apply Estimates → populate form fields ────────────────────────────────
    document.getElementById('ai-apply-btn')?.addEventListener('click', () => {
        const opt  = document.getElementById('ai-opt-hours')?.textContent;
        const ml   = document.getElementById('ai-ml-hours')?.textContent;
        const pess = document.getElementById('ai-pess-hours')?.textContent;
        const prio = document.getElementById('ai-priority')?.textContent;

        setFieldValue('[name$="EstimatedOptimisticHours"]', opt);
        setFieldValue('[name$="EstimatedMostLikelyHours"]', ml);
        setFieldValue('[name$="EstimatedPessimisticHours"]', pess);

        // Priority dropdown
        const prioSelect = document.querySelector('[name$="Priority"]');
        if (prioSelect && prio) {
            const matchOpt = Array.from(prioSelect.options).find(o => o.text === prio);
            if (matchOpt) prioSelect.value = matchOpt.value;
        }

        // Flash applied fields
        ['EstimatedOptimisticHours', 'EstimatedMostLikelyHours', 'EstimatedPessimisticHours'].forEach(fn => {
            const el = document.querySelector(`[name$="${fn}"]`);
            if (el) {
                el.classList.remove('ai-applied');
                void el.offsetWidth; // force reflow
                el.classList.add('ai-applied');
                el.addEventListener('animationend', () => el.classList.remove('ai-applied'), { once: true });
            }
        });
    });

    // ── Create selected children ──────────────────────────────────────────────
    document.getElementById('ai-create-children-btn')?.addEventListener('click', async () => {
        const checked = document.querySelectorAll('.ai-child-item input[type=checkbox]:checked');
        if (!checked.length) return;

        const parentId = getParentId();
        const items = Array.from(checked).map(cb => {
            const row = cb.closest('.ai-child-row');
            return {
                entityType:         childType,
                parentId,
                title:              row.dataset.title,
                description:        row.dataset.description || null,
                acceptanceCriteria: row.dataset.ac || null,
                priority:           row.dataset.priority || 'Medium',
                optimisticHours:    parseFloat(row.dataset.opt)  || null,
                mostLikelyHours:    parseFloat(row.dataset.ml)   || null,
                pessimisticHours:   parseFloat(row.dataset.pess) || null,
            };
        });

        const btn = document.getElementById('ai-create-children-btn');
        btn.disabled = true;
        btn.textContent = 'Creating...';

        try {
            const result = await fetch('/api/ai/bulk-create', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getAntiForgeryToken()
                },
                body: JSON.stringify({ items })
            }).then(r => { if (!r.ok) throw new Error('Bulk create failed.'); return r.json(); });

            // Store created IDs in sessionStorage for badge display after redirect
            if (result.createdIds?.length) {
                sessionStorage.setItem('ai_created_ids', JSON.stringify(result.createdIds));
                sessionStorage.setItem('ai_created_type', result.entityType);
            }

            // Redirect to parent detail page
            const redirect = getDetailUrl();
            if (redirect) {
                window.location.href = redirect;
            } else {
                btn.textContent = `✓ Created ${result.createdIds?.length ?? 0} items`;
            }
        } catch (err) {
            btn.disabled = false;
            btn.textContent = `Create Selected (${checked.length})`;
            showError('Failed to create items: ' + err.message);
        }
    });

    // ── Checkbox counter (keyboard + click accessible) ────────────────────────
    document.getElementById('ai-children-list')?.addEventListener('change', e => {
        if (e.target.type !== 'checkbox') return;
        updateCounter();
    });

    document.getElementById('ai-children-list')?.addEventListener('keydown', e => {
        if (e.target.type === 'checkbox' && e.key === ' ') {
            e.target.checked = !e.target.checked;
            e.target.dispatchEvent(new Event('change', { bubbles: true }));
            e.preventDefault();
        }
    });

    document.getElementById('ai-select-all-btn')?.addEventListener('click', () => {
        document.querySelectorAll('.ai-child-item input[type=checkbox]').forEach(cb => cb.checked = true);
        updateCounter();
    });

    document.getElementById('ai-deselect-all-btn')?.addEventListener('click', () => {
        document.querySelectorAll('.ai-child-item input[type=checkbox]').forEach(cb => cb.checked = false);
        updateCounter();
    });

    function updateCounter() {
        const count = document.querySelectorAll('.ai-child-item input[type=checkbox]:checked').length;
        const countEl = document.getElementById('ai-selected-count');
        const createBtn = document.getElementById('ai-create-children-btn');
        if (countEl) countEl.textContent = count;
        if (createBtn) createBtn.disabled = count === 0;
    }

    // ── Render functions ──────────────────────────────────────────────────────
    function renderEstimation(r) {
        setText('ai-opt-hours',    r.optimisticHours?.toFixed(1) ?? '-');
        setText('ai-ml-hours',     r.mostLikelyHours?.toFixed(1) ?? '-');
        setText('ai-pess-hours',   r.pessimisticHours?.toFixed(1) ?? '-');
        setText('ai-pert-hours',   r.pertHours?.toFixed(1) ?? '-');
        setText('ai-priority',     r.priority ?? '-');
        setText('ai-story-points', r.storyPoints != null ? String(r.storyPoints) : '-');
        setText('ai-budget',       r.estimatedBudgetBdt?.toLocaleString('en-BD') ?? '-');
        setText('ai-rationale',    r.rationale ?? '');

        const badge = document.getElementById('ai-confidence-badge');
        if (badge) {
            badge.textContent = r.confidence ?? 'Low';
            badge.className = `ai-badge ai-badge--${(r.confidence ?? 'low').toLowerCase()}`;
        }

        if (r.risks?.length) {
            const list = document.getElementById('ai-risks-list');
            if (list) list.innerHTML = r.risks.map(risk => `<li>${escHtml(risk)}</li>`).join('');
            const risksBlock = document.getElementById('ai-risks-block');
            if (risksBlock) risksBlock.style.display = '';
        }

        showResults();
    }

    function renderChildren(suggestions) {
        const list = document.getElementById('ai-children-list');
        if (!list) return;

        const children = suggestions.children ?? suggestions.features ?? [];
        if (!children.length) {
            list.innerHTML = '<p class="text-muted small p-2">No suggestions available.</p>';
            document.getElementById('ai-children').style.display = '';
            return;
        }

        list.innerHTML = children.map((child) => `
            <div class="ai-child-row" role="listitem"
                 data-title="${escAttr(child.title ?? '')}"
                 data-description="${escAttr(child.description ?? '')}"
                 data-ac="${escAttr(child.acceptanceCriteria ?? '')}"
                 data-priority="${escAttr(child.priority ?? 'Medium')}"
                 data-opt="${child.optimisticHours ?? ''}"
                 data-ml="${child.mostLikelyHours ?? ''}"
                 data-pess="${child.pessimisticHours ?? ''}">
                <label class="ai-child-item">
                    <input type="checkbox" checked aria-label="Select: ${escAttr(child.title ?? '')}" />
                    <div class="ai-child-info">
                        <strong>${escHtml(child.title ?? '')}</strong>
                        <span class="ai-child-desc">${escHtml(child.description ?? '')}</span>
                        ${child.mostLikelyHours ? `<span class="ai-child-hours">~${child.mostLikelyHours}h</span>` : ''}
                        <span class="ai-badge ai-badge--priority-${(child.priority ?? 'medium').toLowerCase()}">${escHtml(child.priority ?? 'Medium')}</span>
                    </div>
                </label>
            </div>
        `).join('');

        updateCounter();
        document.getElementById('ai-children').style.display = '';
    }

    function renderFullCascade(result) {
        // Full cascade returns nested structure; flatten to display as selectable list
        const list = document.getElementById('ai-children-list');
        if (!list) return;

        const items = result.features ?? result.epics ?? result.items ?? [];
        if (!items.length) {
            list.innerHTML = '<p class="text-muted small p-2">No cascade items returned.</p>';
            document.getElementById('ai-children').style.display = '';
            return;
        }

        // Flatten features (and their nested children) for display
        const flat = [];
        function flatten(node, level) {
            flat.push({ ...node, _level: level });
            (node.userStories ?? node.features ?? node.children ?? []).forEach(c => flatten(c, level + 1));
        }
        items.forEach(i => flatten(i, 0));

        list.innerHTML = flat.map(child => `
            <div class="ai-child-row" role="listitem" style="margin-left:${child._level * 1}rem"
                 data-title="${escAttr(child.title ?? '')}"
                 data-description="${escAttr(child.description ?? '')}"
                 data-ac="${escAttr(child.acceptanceCriteria ?? '')}"
                 data-priority="${escAttr(child.priority ?? 'Medium')}"
                 data-opt="" data-ml="${child.estimatedHours ?? ''}" data-pess="">
                <label class="ai-child-item">
                    <input type="checkbox" checked aria-label="Select: ${escAttr(child.title ?? '')}" />
                    <div class="ai-child-info">
                        <strong>${escHtml(child.title ?? '')}</strong>
                        <span class="ai-child-desc">${escHtml(child.description ?? '')}</span>
                        ${child.estimatedHours ? `<span class="ai-child-hours">~${child.estimatedHours}h</span>` : ''}
                    </div>
                </label>
            </div>
        `).join('');

        updateCounter();
        document.getElementById('ai-children').style.display = '';
    }

    // ── Utilities ─────────────────────────────────────────────────────────────
    async function fetchEstimation(title, description) {
        const resp = await fetch('/api/ai/estimate', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify({
                entityType,
                title,
                description,
                projectId:   panel.dataset.projectId   ? parseInt(panel.dataset.projectId)   : null,
                epicId:      panel.dataset.epicId      ? parseInt(panel.dataset.epicId)      : null,
                featureId:   panel.dataset.featureId   ? parseInt(panel.dataset.featureId)   : null,
                userStoryId: panel.dataset.userStoryId ? parseInt(panel.dataset.userStoryId) : null,
            })
        });
        if (!resp.ok) throw new Error(await resp.text());
        return resp.json();
    }

    async function fetchChildren(title, description, mode) {
        const resp = await fetch('/api/ai/suggest-children', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify({
                parentType:        entityType,
                parentId:          parseInt(entityId ?? '0'),
                parentTitle:       title,
                parentDescription: description,
                projectId:         panel.dataset.projectId ? parseInt(panel.dataset.projectId) : null,
                stepByStep:        mode === 'step'
            })
        });
        if (!resp.ok) throw new Error(await resp.text());
        return resp.json();
    }

    function getDescriptionValue() {
        return document.querySelector(
            '[name="Epic.Description"], [name="Feature.Description"], ' +
            '[name="UserStory.Description"], [name="TaskItem.Description"], ' +
            '[name="Project.Description"]'
        )?.value ?? '';
    }

    function showLoading() {
        const loading  = document.getElementById('ai-loading');
        const results  = document.getElementById('ai-results');
        const errorEl  = document.getElementById('ai-error');
        const children = document.getElementById('ai-children');
        if (loading)  loading.style.display = '';
        if (results)  results.style.display = 'none';
        if (errorEl)  errorEl.style.display = 'none';
        if (children) children.style.display = 'none';
    }

    function showResults() {
        const loading = document.getElementById('ai-loading');
        const results = document.getElementById('ai-results');
        const errorEl = document.getElementById('ai-error');
        if (loading) loading.style.display = 'none';
        if (results) results.style.display = '';
        if (errorEl) errorEl.style.display = 'none';
    }

    function showError(msg) {
        const loading  = document.getElementById('ai-loading');
        const msgEl    = document.getElementById('ai-error-msg');
        const errorEl  = document.getElementById('ai-error');
        if (loading) loading.style.display = 'none';
        if (msgEl)   msgEl.textContent = msg;
        if (errorEl) errorEl.style.display = '';
    }

    function setText(id, value) {
        const el = document.getElementById(id);
        if (el) el.textContent = value;
    }

    function setFieldValue(selector, value) {
        const el = document.querySelector(selector);
        if (el && value && value !== '-') {
            el.value = value;
            el.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }

    function getAntiForgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    }

    function getParentId() {
        // Priority: epicId for Feature, featureId for UserStory, userStoryId for Task
        const pid = panel.dataset.epicId      ||
                    panel.dataset.featureId   ||
                    panel.dataset.userStoryId ||
                    panel.dataset.projectId   || '0';
        return parseInt(pid);
    }

    function getDetailUrl() {
        const map = {
            Feature:   `/Epics/Details/${panel.dataset.epicId}`,
            UserStory: `/Features/Details/${panel.dataset.featureId}`,
            Task:      `/UserStories/Details/${panel.dataset.userStoryId}`,
            Epic:      `/Projects/Details/${panel.dataset.projectId}`
        };
        return map[childType] ?? null;
    }

    function debounce(fn, ms) {
        let t;
        return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
    }

    function escHtml(s) {
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function escAttr(s) {
        return String(s).replace(/"/g, '&quot;');
    }

    // ── AI-Generated badge from sessionStorage (set by bulk-create) ───────────
    (function checkAiBadge() {
        const ids  = JSON.parse(sessionStorage.getItem('ai_created_ids') ?? 'null');
        const type = sessionStorage.getItem('ai_created_type');
        if (!ids || !type) return;

        sessionStorage.removeItem('ai_created_ids');
        sessionStorage.removeItem('ai_created_type');

        // Mark rows in list views that match created IDs
        ids.forEach(id => {
            const link = document.querySelector(`[data-ai-id="${id}"]`);
            if (link) {
                const badge = document.createElement('span');
                badge.className = 'ai-badge ai-badge--new ms-2';
                badge.textContent = '✨ AI';
                link.appendChild(badge);
            }
        });
    })();
})();
