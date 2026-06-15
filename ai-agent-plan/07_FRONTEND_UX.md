# Frontend UX — Razor Partials, JavaScript, UI Components
**OfficeTaskManagement · AI Estimation Panel + Copilot Sidebar**

---

## Design Principles

- **Zero new npm dependencies** — Vanilla JS + existing CSS patterns
- **Non-blocking** — AI panel loads asynchronously; form is always usable without it
- **Progressive enhancement** — if Gemini is unreachable, panel shows graceful error; form still works
- **Apply = auto-fill** — clicking "Apply" populates actual form fields (not just display values)
- **Debounced trigger** — 500ms after user stops typing in Title field

---

## Shared Partial: `_AiEstimationPanel.cshtml`

**Location**: `Views/Shared/_AiEstimationPanel.cshtml`  
**Used by**: Epics/Create, Epics/Edit, Features/Create, Features/Edit, UserStories/Create, UserStories/Edit, TaskItems/Create, TaskItems/Edit, Projects/Create, Projects/Edit

```html
@* Parameters injected via ViewData *@
@{
    var entityType  = ViewData["AiEntityType"] as string ?? "Task";
    var projectId   = ViewData["AiProjectId"] as int?;
    var epicId      = ViewData["AiEpicId"] as int?;
    var featureId   = ViewData["AiFeatureId"] as int?;
    var userStoryId = ViewData["AiUserStoryId"] as int?;
    var entityId    = ViewData["AiEntityId"] as int?;   // null = new item
    var childType   = ViewData["AiChildType"] as string; // e.g. "Feature" for an Epic
}

<div id="ai-panel" class="ai-panel" data-entity-type="@entityType"
     data-project-id="@projectId" data-epic-id="@epicId"
     data-feature-id="@featureId" data-user-story-id="@userStoryId"
     data-entity-id="@entityId" data-child-type="@childType">

    <!-- Panel Header -->
    <div class="ai-panel__header">
        <span class="ai-panel__icon">✨</span>
        <span class="ai-panel__title">AI Estimation Assistant</span>
        <button type="button" id="ai-analyze-btn" class="ai-panel__trigger-btn" disabled>
            Analyze with AI ▶
        </button>
        @if (entityId.HasValue)
        {
            <button type="button" id="ai-reestimate-btn" class="ai-panel__reestimate-btn">
                ↺ Re-estimate
            </button>
        }
    </div>

    <!-- Loading state -->
    <div id="ai-loading" class="ai-panel__loading" style="display:none">
        <div class="ai-spinner"></div>
        <span>Analyzing project context...</span>
    </div>

    <!-- Error state -->
    <div id="ai-error" class="ai-panel__error" style="display:none">
        <span id="ai-error-msg"></span>
    </div>

    <!-- Estimation results -->
    <div id="ai-results" class="ai-panel__results" style="display:none">
        
        <!-- Confidence badge + Rationale -->
        <div class="ai-panel__rationale">
            <span id="ai-confidence-badge" class="ai-badge"></span>
            <p id="ai-rationale"></p>
        </div>

        <!-- PERT Estimate Row -->
        <div class="ai-panel__estimates">
            <div class="ai-est-block">
                <label>Optimistic</label>
                <span id="ai-opt-hours"></span>h
            </div>
            <div class="ai-est-block ai-est-block--main">
                <label>Most Likely</label>
                <span id="ai-ml-hours"></span>h
            </div>
            <div class="ai-est-block">
                <label>Pessimistic</label>
                <span id="ai-pess-hours"></span>h
            </div>
            <div class="ai-est-block ai-est-block--pert">
                <label>PERT</label>
                <span id="ai-pert-hours"></span>h
            </div>
        </div>

        <!-- Meta row -->
        <div class="ai-panel__meta">
            <span>Priority: <strong id="ai-priority"></strong></span>
            <span>Story Points: <strong id="ai-story-points"></strong></span>
            <span>Est. Budget: <strong id="ai-budget"></strong> BDT</span>
        </div>

        <!-- Risks -->
        <div id="ai-risks-block" class="ai-panel__risks" style="display:none">
            <strong>⚠ Risks:</strong>
            <ul id="ai-risks-list"></ul>
        </div>

        <!-- Apply Estimates button -->
        <button type="button" id="ai-apply-btn" class="ai-panel__apply-btn">
            ✓ Apply These Estimates to Form
        </button>
    </div>

    <!-- Child suggestions (only if childType is set) -->
    @if (!string.IsNullOrEmpty(childType))
    {
        <div id="ai-children" class="ai-panel__children" style="display:none">
            <div class="ai-panel__children-header">
                <strong>✨ Suggested <span class="child-type-label">@childType</span>s</strong>
                <div class="ai-children-mode">
                    <label>
                        <input type="radio" name="ai-depth" value="step" checked /> Step-by-step
                    </label>
                    <label>
                        <input type="radio" name="ai-depth" value="full" /> Full breakdown
                    </label>
                </div>
            </div>
            <div id="ai-children-list" class="ai-children-list">
                <!-- Populated by JS -->
            </div>
            <div class="ai-panel__children-actions">
                <button type="button" id="ai-select-all-btn" class="ai-btn-link">Select All</button>
                <button type="button" id="ai-deselect-all-btn" class="ai-btn-link">Deselect All</button>
                <button type="button" id="ai-create-children-btn" class="ai-panel__create-btn" disabled>
                    Create Selected (<span id="ai-selected-count">0</span>)
                </button>
            </div>
        </div>
    }
</div>
```

---

## JavaScript: `wwwroot/js/ai-panel.js`

```javascript
/**
 * AI Estimation Panel — OfficeTaskManagement
 * Handles: trigger detection, API calls, form field population, child creation
 */
(function () {
    'use strict';

    const panel     = document.getElementById('ai-panel');
    if (!panel) return;

    const entityType  = panel.dataset.entityType;
    const childType   = panel.dataset.childType;
    const entityId    = panel.dataset.entityId || null;

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
        const description = document.querySelector(
            '[name="Epic.Description"], [name="Feature.Description"], ' +
            '[name="UserStory.Description"], [name="TaskItem.Description"], ' +
            '[name="Project.Description"]'
        )?.value ?? '';

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
        }
    });

    // ── Re-estimate button ────────────────────────────────────────────────────
    document.getElementById('ai-reestimate-btn')?.addEventListener('click', async () => {
        const title = titleInput?.value.trim() ?? '';
        const originalHours = parseFloat(
            document.querySelector('[name="TaskItem.EstimatedHours"]')?.value ?? '0'
        );
        showLoading();
        try {
            const result = await fetch('/api/ai/reestimate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiForgeryToken() },
                body: JSON.stringify({
                    entityType, entityId: parseInt(entityId),
                    title, originalEstimatedHours: originalHours
                })
            }).then(r => r.json());
            renderEstimation(result);
        } catch (err) {
            showError('Re-estimation failed. ' + err.message);
        }
    });

    // ── Depth mode switch (step vs full) ─────────────────────────────────────
    document.querySelectorAll('[name="ai-depth"]').forEach(radio => {
        radio.addEventListener('change', async () => {
            if (radio.value === 'full') {
                showLoading();
                const title = titleInput?.value.trim() ?? '';
                const result = await fetch('/api/ai/full-cascade', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiForgeryToken() },
                    body: JSON.stringify({ parentType: entityType, parentId: parseInt(entityId ?? '0'), parentTitle: title })
                }).then(r => r.json());
                renderFullCascade(result);
            }
        });
    });

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
            const opt = Array.from(prioSelect.options).find(o => o.text === prio);
            if (opt) prioSelect.value = opt.value;
        }

        // Flash applied fields
        ['EstimatedOptimisticHours','EstimatedMostLikelyHours','EstimatedPessimisticHours'].forEach(fn => {
            document.querySelector(`[name$="${fn}"]`)?.classList.add('ai-applied');
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
                entityType: childType,
                parentId,
                title:            row.dataset.title,
                description:      row.dataset.description,
                acceptanceCriteria: row.dataset.ac,
                priority:         row.dataset.priority,
                optimisticHours:  parseFloat(row.dataset.opt  || '0') || null,
                mostLikelyHours:  parseFloat(row.dataset.ml   || '0') || null,
                pessimisticHours: parseFloat(row.dataset.pess || '0') || null,
            };
        });

        const btn = document.getElementById('ai-create-children-btn');
        btn.disabled = true;
        btn.textContent = 'Creating...';

        try {
            const result = await fetch('/api/ai/bulk-create', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiForgeryToken() },
                body: JSON.stringify({ items })
            }).then(r => r.json());

            // Redirect to parent detail page to show created children
            const redirect = getDetailUrl();
            if (redirect) window.location.href = redirect;
        } catch (err) {
            btn.disabled = false;
            btn.textContent = `Create Selected (${checked.length})`;
            showError('Failed to create items: ' + err.message);
        }
    });

    // ── Checkbox counter ──────────────────────────────────────────────────────
    document.getElementById('ai-children-list')?.addEventListener('change', e => {
        if (e.target.type !== 'checkbox') return;
        const count = document.querySelectorAll('.ai-child-item input:checked').length;
        document.getElementById('ai-selected-count').textContent = count;
        document.getElementById('ai-create-children-btn').disabled = count === 0;
    });

    document.getElementById('ai-select-all-btn')?.addEventListener('click', () => {
        document.querySelectorAll('.ai-child-item input').forEach(cb => cb.checked = true);
        document.getElementById('ai-selected-count').textContent =
            document.querySelectorAll('.ai-child-item input').length;
        document.getElementById('ai-create-children-btn').disabled = false;
    });

    document.getElementById('ai-deselect-all-btn')?.addEventListener('click', () => {
        document.querySelectorAll('.ai-child-item input').forEach(cb => cb.checked = false);
        document.getElementById('ai-selected-count').textContent = '0';
        document.getElementById('ai-create-children-btn').disabled = true;
    });

    // ── Render functions ──────────────────────────────────────────────────────
    function renderEstimation(r) {
        document.getElementById('ai-opt-hours').textContent  = r.optimisticHours?.toFixed(1) ?? '-';
        document.getElementById('ai-ml-hours').textContent   = r.mostLikelyHours?.toFixed(1) ?? '-';
        document.getElementById('ai-pess-hours').textContent = r.pessimisticHours?.toFixed(1) ?? '-';
        document.getElementById('ai-pert-hours').textContent = r.pertHours?.toFixed(1) ?? '-';
        document.getElementById('ai-priority').textContent   = r.priority ?? '-';
        document.getElementById('ai-story-points').textContent = r.storyPoints ?? '-';
        document.getElementById('ai-budget').textContent     = r.estimatedBudgetBDT?.toLocaleString('en-BD') ?? '-';
        document.getElementById('ai-rationale').textContent  = r.rationale ?? '';

        const badge = document.getElementById('ai-confidence-badge');
        badge.textContent = r.confidence;
        badge.className = `ai-badge ai-badge--${(r.confidence ?? 'low').toLowerCase()}`;

        if (r.risks?.length) {
            const list = document.getElementById('ai-risks-list');
            list.innerHTML = r.risks.map(risk => `<li>${escHtml(risk)}</li>`).join('');
            document.getElementById('ai-risks-block').style.display = '';
        }

        showResults();
    }

    function renderChildren(suggestions) {
        const list = document.getElementById('ai-children-list');
        if (!list) return;
        list.innerHTML = suggestions.children.map((child, i) => `
            <div class="ai-child-row" 
                 data-title="${escAttr(child.title)}"
                 data-description="${escAttr(child.description ?? '')}"
                 data-ac="${escAttr(child.acceptanceCriteria ?? '')}"
                 data-priority="${escAttr(child.priority ?? 'Medium')}"
                 data-opt="${child.optimisticHours ?? ''}"
                 data-ml="${child.mostLikelyHours ?? ''}"
                 data-pess="${child.pessimisticHours ?? ''}">
                <label class="ai-child-item">
                    <input type="checkbox" checked />
                    <div class="ai-child-info">
                        <strong>${escHtml(child.title)}</strong>
                        <span class="ai-child-desc">${escHtml(child.description ?? '')}</span>
                        ${child.mostLikelyHours ? `<span class="ai-child-hours">~${child.mostLikelyHours}h</span>` : ''}
                        <span class="ai-badge ai-badge--priority-${(child.priority ?? 'medium').toLowerCase()}">${child.priority ?? 'Medium'}</span>
                    </div>
                </label>
            </div>
        `).join('');

        // Trigger counter update
        const count = suggestions.children.length;
        document.getElementById('ai-selected-count').textContent = count;
        document.getElementById('ai-create-children-btn').disabled = false;
        document.getElementById('ai-children').style.display = '';
    }

    // ── Utilities ─────────────────────────────────────────────────────────────
    async function fetchEstimation(title, description) {
        const resp = await fetch('/api/ai/estimate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiForgeryToken() },
            body: JSON.stringify({
                entityType,
                title, description,
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
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiForgeryToken() },
            body: JSON.stringify({
                parentType: entityType,
                parentId:   parseInt(entityId ?? '0'),
                parentTitle: title,
                parentDescription: description,
                projectId:  panel.dataset.projectId ? parseInt(panel.dataset.projectId) : null,
                stepByStep: mode === 'step'
            })
        });
        if (!resp.ok) throw new Error(await resp.text());
        return resp.json();
    }

    function showLoading() {
        document.getElementById('ai-loading').style.display = '';
        document.getElementById('ai-results').style.display = 'none';
        document.getElementById('ai-error').style.display = 'none';
        document.getElementById('ai-children').style.display = 'none';
    }
    function showResults() {
        document.getElementById('ai-loading').style.display = 'none';
        document.getElementById('ai-results').style.display = '';
        document.getElementById('ai-error').style.display = 'none';
    }
    function showError(msg) {
        document.getElementById('ai-loading').style.display = 'none';
        document.getElementById('ai-error-msg').textContent = msg;
        document.getElementById('ai-error').style.display = '';
    }
    function setFieldValue(selector, value) {
        const el = document.querySelector(selector);
        if (el && value) { el.value = value; el.dispatchEvent(new Event('change')); }
    }
    function getAntiForgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    }
    function getParentId() {
        return parseInt(panel.dataset.epicId ?? panel.dataset.featureId ??
               panel.dataset.userStoryId ?? panel.dataset.projectId ?? '0');
    }
    function getDetailUrl() {
        // Build redirect URL based on entity type
        const map = { Feature: `/Epics/Details/${panel.dataset.epicId}`,
                      UserStory: `/Features/Details/${panel.dataset.featureId}`,
                      Task: `/UserStories/Details/${panel.dataset.userStoryId}`,
                      Epic: `/Projects/Details/${panel.dataset.projectId}` };
        return map[childType] ?? null;
    }
    function debounce(fn, ms) {
        let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
    }
    function escHtml(s) {
        return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }
    function escAttr(s) { return s.replace(/"/g, '&quot;'); }
})();
```

---

## How to Inject the Panel into Existing Views

In each Create/Edit Razor view, add at the top of the form section:

```razor
@* Example: Views/Epics/Create.cshtml *@
@{
    ViewData["AiEntityType"] = "Epic";
    ViewData["AiProjectId"]  = Model.Epic.ProjectId;  // or from ViewBag
    ViewData["AiChildType"]  = "Feature";             // what children to suggest
}

@await Html.PartialAsync("_AiEstimationPanel")

@* existing form continues below... *@
<form asp-action="Create">
    ...
</form>
```

For Edit views, also add:
```razor
ViewData["AiEntityId"] = Model.Epic.Id;
```

### Entity → ViewData Map

| View | AiEntityType | AiChildType | AiParentId keys |
|------|-------------|-------------|----------------|
| Projects/Create | Project | Epic | — |
| Projects/Edit | Project | Epic | AiEntityId |
| Epics/Create | Epic | Feature | AiProjectId |
| Epics/Edit | Epic | Feature | AiProjectId, AiEntityId |
| Features/Create | Feature | UserStory | AiEpicId |
| Features/Edit | Feature | UserStory | AiEpicId, AiEntityId |
| UserStories/Create | UserStory | Task | AiFeatureId |
| UserStories/Edit | UserStory | Task | AiFeatureId, AiEntityId |
| TaskItems/Create | Task | — (no children) | AiUserStoryId |
| TaskItems/Edit | Task | — | AiUserStoryId, AiEntityId |

---

## CSS: AI Panel Styles (add to `wwwroot/css/site.css`)

```css
/* ── AI Estimation Panel ───────────────────────────────── */
.ai-panel {
    border: 1px solid #6c47ff33;
    border-radius: 12px;
    background: linear-gradient(135deg, #0f0a1e 0%, #1a1040 100%);
    padding: 1rem 1.25rem;
    margin-bottom: 1.5rem;
    color: #e0d9ff;
    box-shadow: 0 4px 24px #6c47ff22;
    transition: box-shadow 0.3s ease;
}
.ai-panel:hover { box-shadow: 0 6px 32px #6c47ff44; }

.ai-panel__header { display: flex; align-items: center; gap: 0.75rem; }
.ai-panel__icon { font-size: 1.25rem; }
.ai-panel__title { font-weight: 600; color: #b39dff; flex: 1; }

.ai-panel__trigger-btn {
    background: linear-gradient(90deg, #6c47ff, #a855f7);
    color: white; border: none; border-radius: 8px;
    padding: 0.4rem 1rem; cursor: pointer; font-size: 0.875rem;
    transition: opacity 0.2s, transform 0.1s;
}
.ai-panel__trigger-btn:disabled { opacity: 0.4; cursor: not-allowed; }
.ai-panel__trigger-btn:not(:disabled):hover { opacity: 0.9; transform: translateY(-1px); }

.ai-panel__reestimate-btn {
    background: transparent; border: 1px solid #6c47ff88;
    color: #b39dff; border-radius: 8px; padding: 0.4rem 0.75rem;
    cursor: pointer; font-size: 0.8rem;
}

.ai-panel__loading { display: flex; align-items: center; gap: 0.5rem; padding: 0.75rem 0; color: #b39dff; }
.ai-spinner {
    width: 18px; height: 18px; border: 2px solid #6c47ff44;
    border-top-color: #6c47ff; border-radius: 50%;
    animation: ai-spin 0.8s linear infinite;
}
@keyframes ai-spin { to { transform: rotate(360deg); } }

.ai-panel__error { color: #ff6b6b; padding: 0.5rem 0; font-size: 0.875rem; }

.ai-panel__rationale { margin: 0.75rem 0 0.5rem; font-size: 0.85rem; color: #9e8ccc; }
.ai-badge { border-radius: 4px; padding: 0.15rem 0.5rem; font-size: 0.75rem; font-weight: 600; }
.ai-badge--high, .ai-badge--critical { background: #ff6b6b22; color: #ff6b6b; }
.ai-badge--medium { background: #fbbf2422; color: #fbbf24; }
.ai-badge--low  { background: #22c55e22; color: #22c55e; }
.ai-badge--priority-high, .ai-badge--priority-critical { background: #ff6b6b22; color: #ff6b6b; }
.ai-badge--priority-medium { background: #fbbf2422; color: #fbbf24; }
.ai-badge--priority-low { background: #22c55e22; color: #22c55e; }

.ai-panel__estimates {
    display: flex; gap: 0.75rem; margin: 0.75rem 0;
    background: #ffffff08; border-radius: 8px; padding: 0.75rem;
}
.ai-est-block { flex: 1; text-align: center; }
.ai-est-block label { display: block; font-size: 0.7rem; color: #7c6ca8; margin-bottom: 0.25rem; }
.ai-est-block span { font-size: 1.1rem; font-weight: 700; color: #d4c8ff; }
.ai-est-block--pert span { color: #6c47ff; }
.ai-est-block--main span { color: #a855f7; }

.ai-panel__meta { display: flex; gap: 1rem; font-size: 0.82rem; color: #8878b0; margin: 0.5rem 0; }

.ai-panel__risks { margin: 0.5rem 0; font-size: 0.82rem; }
.ai-panel__risks ul { margin: 0.25rem 0 0 1rem; color: #fbbf24; }

.ai-panel__apply-btn {
    background: linear-gradient(90deg, #22c55e, #16a34a);
    color: white; border: none; border-radius: 8px;
    padding: 0.5rem 1.25rem; cursor: pointer; font-size: 0.875rem;
    margin-top: 0.5rem; transition: opacity 0.2s;
}
.ai-panel__apply-btn:hover { opacity: 0.9; }

/* Fields that were AI-applied get a brief highlight */
.ai-applied { animation: ai-flash 1s ease-out; }
@keyframes ai-flash {
    0%   { background: #6c47ff33; }
    100% { background: transparent; }
}

/* ── Child Suggestions ─────────────────────────────── */
.ai-panel__children { border-top: 1px solid #6c47ff22; margin-top: 1rem; padding-top: 1rem; }
.ai-panel__children-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 0.75rem; }
.ai-children-mode { display: flex; gap: 0.75rem; font-size: 0.8rem; color: #8878b0; }

.ai-child-row { border: 1px solid #ffffff11; border-radius: 8px; padding: 0.6rem 0.75rem; margin-bottom: 0.4rem; }
.ai-child-item { display: flex; align-items: flex-start; gap: 0.5rem; cursor: pointer; }
.ai-child-info { display: flex; flex-wrap: wrap; gap: 0.25rem; align-items: center; }
.ai-child-info strong { width: 100%; color: #d4c8ff; }
.ai-child-desc { font-size: 0.78rem; color: #7c6ca8; width: 100%; }
.ai-child-hours { font-size: 0.78rem; background: #6c47ff22; color: #b39dff; border-radius: 4px; padding: 0.1rem 0.4rem; }

.ai-panel__children-actions { display: flex; align-items: center; gap: 0.75rem; margin-top: 0.75rem; }
.ai-btn-link { background: none; border: none; color: #7c6ca8; cursor: pointer; font-size: 0.8rem; text-decoration: underline; }
.ai-panel__create-btn {
    background: linear-gradient(90deg, #6c47ff, #a855f7);
    color: white; border: none; border-radius: 8px;
    padding: 0.5rem 1.25rem; cursor: pointer; font-size: 0.875rem;
    margin-left: auto; transition: opacity 0.2s;
}
.ai-panel__create-btn:disabled { opacity: 0.4; cursor: not-allowed; }
.ai-panel__create-btn:not(:disabled):hover { opacity: 0.9; }
```

---

## Script Registration

Add to `Views/Shared/_Layout.cshtml` at bottom of `<body>`:

```html
<script src="~/js/ai-panel.js" asp-append-version="true"></script>
```
