/**
 * wizard-ui.js
 * Shared DOM render utilities used across all steps.
 * All functions return DOM elements or HTML strings — no side effects.
 */

import { WizardState } from './wizard-state.js';

// ── Step progress nav ─────────────────────────────────────────────────────

export function updateStepNav(currentStep, totalSteps) {
    const progressBar = document.getElementById('step-progress-bar');
    const pct = ((currentStep - 1) / (totalSteps - 1)) * 100;
    if (progressBar) progressBar.style.width = `${pct}%`;

    for (let i = 1; i <= totalSteps; i++) {
        const node = document.querySelector(`.wizard-step-node[data-step="${i}"]`);
        if (!node) continue;
        node.classList.remove('active', 'completed');
        if (i < currentStep)  node.classList.add('completed');
        if (i === currentStep) node.classList.add('active');
    }

    // Prev / Next buttons
    const btnPrev = document.getElementById('btn-wizard-prev');
    const btnNext = document.getElementById('btn-wizard-next');
    if (btnPrev) btnPrev.disabled = currentStep === 1;

    if (btnNext) {
        const isLast = currentStep === totalSteps;
        btnNext.innerHTML = isLast
            ? '<i class="fas fa-rocket"></i> Confirm &amp; Initiate Project'
            : 'Next Step <i class="fas fa-chevron-right"></i>';
        btnNext.className = isLast ? 'btn btn-success' : 'btn btn-primary';
    }
}

// ── Footer summary bar ─────────────────────────────────────────────────────

export function updateSummaryBar() {
    const bar = document.getElementById('ow-summary-bar');
    if (!bar) return;
    const s = WizardState.getSummary();
    bar.innerHTML = `
        <span class="ow-summary-stat"><i class="fas fa-layer-group" style="color:var(--primary-color)"></i> <strong>${s.epics}</strong> Epics</span>
        <span class="ow-summary-stat"><i class="fas fa-puzzle-piece" style="color:#6264A7"></i> <strong>${s.features}</strong> Features</span>
        <span class="ow-summary-stat"><i class="fas fa-book" style="color:var(--warning-color)"></i> <strong>${s.stories}</strong> Stories</span>
        <span class="ow-summary-stat"><i class="fas fa-tasks" style="color:var(--success-color)"></i> <strong>${s.tasks}</strong> Tasks</span>
        <span class="ow-summary-stat"><i class="fas fa-vial" style="color:var(--text-secondary)"></i> <strong>${s.tests}</strong> Tests</span>`;
}

// ── Checkpoint flash ──────────────────────────────────────────────────────

export function flashCheckpoint() {
    const badge = document.getElementById('ow-checkpoint-badge');
    if (!badge) return;
    badge.classList.add('visible');
    setTimeout(() => badge.classList.remove('visible'), 2800);
}

// ── Status badge HTML ────────────────────────────────────────────────────

export function statusBadgeHtml(type, label) {
    // type: 'ai-loading' | 'ai-done' | 'saved' | 'skipped'
    const dot = type === 'ai-loading' ? '<span class="ow-badge-dot"></span>' : '';
    return `<span class="ow-status-badge ${type}">${dot}${label}</span>`;
}

// ── Priority chip HTML ────────────────────────────────────────────────────

export function priorityChipHtml(priority) {
    const cls = {
        Critical: 'ow-priority-critical',
        High:     'ow-priority-high',
        Medium:   'ow-priority-medium',
        Low:      'ow-priority-low'
    }[priority] ?? 'ow-priority-low';
    return `<span class="ow-priority ${cls}">${priority ?? 'Medium'}</span>`;
}

// ── Skeleton rows ─────────────────────────────────────────────────────────

export function skeletonRows(count = 3) {
    return Array.from({ length: count }, () => `
        <div class="onboard-list-item" style="pointer-events:none;">
            <div style="width:1.15rem; height:1.15rem; border-radius:3px;" class="ow-skeleton"></div>
            <div class="item-content">
                <div class="ow-skeleton ow-skeleton-line wide"></div>
                <div class="ow-skeleton ow-skeleton-line half"></div>
            </div>
        </div>`).join('');
}

// ── Mini progress slot for parallel loading grid ──────────────────────────

export function createEpicSlot(epic) {
    const div = document.createElement('div');
    div.className = 'ow-epic-slot loading';
    div.dataset.epicId = epic.id;
    div.innerHTML = `
        <div class="ow-epic-slot-header">
            <h5 title="${esc(epic.name)}">${esc(epic.name)}</h5>
            ${statusBadgeHtml('ai-loading', 'Analyzing…')}
        </div>
        <div class="ow-epic-slot-body">
            <div class="ow-slot-progress"><div class="ow-slot-progress-fill"></div></div>
            <div class="ow-slot-loading-text">Gemini is reading code files…</div>
        </div>`;
    return div;
}

export function markEpicSlotDone(epicId, featureCount) {
    const slot = document.querySelector(`.ow-epic-slot[data-epic-id="${epicId}"]`);
    if (!slot) return;
    slot.classList.replace('loading', 'done');
    const badge = slot.querySelector('.ow-status-badge');
    if (badge) { badge.className = 'ow-status-badge saved'; badge.textContent = `✓ ${featureCount} features`; }
    const fill = slot.querySelector('.ow-slot-progress-fill');
    if (fill) fill.style.width = '100%';
    const txt = slot.querySelector('.ow-slot-loading-text');
    if (txt) txt.textContent = `${featureCount} feature${featureCount !== 1 ? 's' : ''} discovered`;
}

export function markEpicSlotError(epicId, message) {
    const slot = document.querySelector(`.ow-epic-slot[data-epic-id="${epicId}"]`);
    if (!slot) return;
    slot.classList.replace('loading', 'error');
    const badge = slot.querySelector('.ow-status-badge');
    if (badge) { badge.className = 'ow-status-badge'; badge.style.cssText = 'background:#FDE7E9;color:var(--danger-color);border:1px solid rgba(232,17,35,.2)'; badge.textContent = '✗ Failed'; }
    const txt = slot.querySelector('.ow-slot-loading-text');
    if (txt) txt.textContent = message;
}

// ── Epic list item ────────────────────────────────────────────────────────

export function renderEpicItem(epic, epicIdx, { onEdit, onDelete }) {
    const div = document.createElement('div');
    div.className = 'onboard-list-item';
    div.dataset.epicIdx = epicIdx;
    if (!epic.selected) div.classList.add('is-skipped');
    div.innerHTML = `
        <input type="checkbox" class="item-checkbox" data-epic-idx="${epicIdx}" ${epic.selected !== false ? 'checked' : ''}>
        <div class="item-content">
            <div class="item-title-row">
                <h6 class="item-title">${esc(epic.name)}</h6>
                <div class="item-actions">
                    <button class="btn-icon-action btn-icon-edit" title="Edit"><i class="fas fa-pencil-alt"></i></button>
                    <button class="btn-icon-action btn-icon-delete" title="Remove"><i class="fas fa-times"></i></button>
                </div>
            </div>
            ${epic.description ? `<p class="item-desc">${esc(epic.description)}</p>` : ''}
        </div>`;
    div.querySelector('.item-checkbox').addEventListener('change', e => {
        epic.selected = e.target.checked;
        div.classList.toggle('is-skipped', !epic.selected);
    });
    div.querySelector('.btn-icon-edit').addEventListener('click', () => onEdit(epicIdx));
    div.querySelector('.btn-icon-delete').addEventListener('click', () => onDelete(epicIdx));
    return div;
}

// ── Feature list item ─────────────────────────────────────────────────────

export function renderFeatureItem(feat, epicIdx, featIdx, { onEdit, onDelete }) {
    const div = document.createElement('div');
    div.className = 'onboard-list-item';
    if (!feat.selected) div.classList.add('is-skipped');
    div.innerHTML = `
        <input type="checkbox" class="item-checkbox" ${feat.selected !== false ? 'checked' : ''}>
        <div class="item-content">
            <div class="item-title-row">
                <h6 class="item-title">${esc(feat.name)}</h6>
                <div class="item-actions">
                    <button class="btn-icon-action btn-icon-edit" title="Edit"><i class="fas fa-pencil-alt"></i></button>
                    <button class="btn-icon-action btn-icon-delete" title="Remove"><i class="fas fa-times"></i></button>
                </div>
            </div>
            ${feat.description ? `<p class="item-desc">${esc(feat.description)}</p>` : ''}
        </div>`;
    div.querySelector('.item-checkbox').addEventListener('change', e => {
        feat.selected = e.target.checked;
        div.classList.toggle('is-skipped', !feat.selected);
    });
    div.querySelector('.btn-icon-edit').addEventListener('click', () => onEdit(epicIdx, featIdx));
    div.querySelector('.btn-icon-delete').addEventListener('click', () => onDelete(epicIdx, featIdx));
    return div;
}

// ── Story list item ───────────────────────────────────────────────────────

export function renderStoryItem(story, epicIdx, featIdx, storyIdx, { onEdit, onDelete }) {
    const div = document.createElement('div');
    div.className = 'onboard-list-item';
    if (!story.selected) div.classList.add('is-skipped');
    div.innerHTML = `
        <input type="checkbox" class="item-checkbox" ${story.selected !== false ? 'checked' : ''}>
        <div class="item-content">
            <div class="item-title-row">
                <h6 class="item-title">${esc(story.title)}</h6>
                <div class="item-actions">
                    ${priorityChipHtml(story.priority)}
                    <button class="btn-icon-action btn-icon-edit" title="Edit"><i class="fas fa-pencil-alt"></i></button>
                    <button class="btn-icon-action btn-icon-delete" title="Remove"><i class="fas fa-times"></i></button>
                </div>
            </div>
            ${story.description ? `<p class="item-desc">${esc(story.description)}</p>` : ''}
            ${story.acceptanceCriteria ? `<p class="item-desc" style="margin-top:.3rem;font-style:italic;color:var(--text-secondary)"><i class="fas fa-check-double" style="font-size:.7rem"></i> ${esc(story.acceptanceCriteria)}</p>` : ''}
        </div>`;
    div.querySelector('.item-checkbox').addEventListener('change', e => {
        story.selected = e.target.checked;
        div.classList.toggle('is-skipped', !story.selected);
    });
    div.querySelector('.btn-icon-edit').addEventListener('click', () => onEdit(epicIdx, featIdx, storyIdx));
    div.querySelector('.btn-icon-delete').addEventListener('click', () => onDelete(epicIdx, featIdx, storyIdx));
    return div;
}

// ── Task row ──────────────────────────────────────────────────────────────

export function renderTaskRow(task, { onEdit }) {
    const pert = calcPert(task.optimisticHours, task.mostLikelyHours, task.pessimisticHours);
    const div = document.createElement('div');
    div.className = 'onboard-list-item';
    div.innerHTML = `
        <input type="checkbox" class="item-checkbox" checked>
        <div class="item-content">
            <div class="item-title-row">
                <h6 class="item-title">${esc(task.title)}</h6>
                <div class="item-actions">
                    ${priorityChipHtml(task.priority)}
                    <button class="btn-icon-action btn-icon-edit" title="Edit hours"><i class="fas fa-pencil-alt"></i></button>
                </div>
            </div>
            <div class="ow-pert-row" style="margin-top:.4rem">
                <div class="ow-pert-input-group">
                    <span class="ow-pert-label">Opt.</span>
                    <span class="ow-pert-val">${task.optimisticHours}h</span>
                </div>
                <div class="ow-pert-input-group">
                    <span class="ow-pert-label">Likely</span>
                    <span class="ow-pert-val">${task.mostLikelyHours}h</span>
                </div>
                <div class="ow-pert-input-group">
                    <span class="ow-pert-label">Pess.</span>
                    <span class="ow-pert-val">${task.pessimisticHours}h</span>
                </div>
                <div class="ow-pert-input-group">
                    <span class="ow-pert-label">PERT</span>
                    <span class="ow-pert-val" style="background:rgba(16,124,16,.1);color:var(--success-color);border-color:rgba(16,124,16,.2)">${pert}h</span>
                </div>
            </div>
        </div>`;
    div.querySelector('.btn-icon-edit').addEventListener('click', () => onEdit(task));
    div.querySelector('.item-checkbox').addEventListener('change', e => {
        task.selected = e.target.checked;
        div.classList.toggle('is-skipped', !task.selected);
    });
    return div;
}

// ── Test case row ─────────────────────────────────────────────────────────

export function renderTestCaseRow(tc, { onEdit }) {
    const div = document.createElement('div');
    div.className = 'onboard-list-item';
    div.innerHTML = `
        <input type="checkbox" class="item-checkbox" checked>
        <div class="item-content">
            <div class="item-title-row">
                <h6 class="item-title"><i class="fas fa-vial" style="color:var(--text-secondary);font-size:.8rem"></i> ${esc(tc.title)}</h6>
                <div class="item-actions">
                    <button class="btn-icon-action btn-icon-edit" title="Edit"><i class="fas fa-pencil-alt"></i></button>
                </div>
            </div>
            ${tc.steps        ? `<p class="item-desc"><strong>Steps:</strong> ${esc(tc.steps)}</p>` : ''}
            ${tc.expectedResult ? `<p class="item-desc"><strong>Expected:</strong> ${esc(tc.expectedResult)}</p>` : ''}
        </div>`;
    div.querySelector('.btn-icon-edit').addEventListener('click', () => onEdit(tc));
    div.querySelector('.item-checkbox').addEventListener('change', e => {
        tc.selected = e.target.checked;
        div.classList.toggle('is-skipped', !tc.selected);
    });
    return div;
}

// ── Accordion builder ─────────────────────────────────────────────────────

export function createAccordion(labelHtml, bodyHtml, badgeHtml = '') {
    const wrapper = document.createElement('div');
    const headerId = `acc-${Math.random().toString(36).slice(2)}`;
    wrapper.innerHTML = `
        <div class="ow-accordion-header" id="${headerId}">
            ${labelHtml}
            ${badgeHtml}
            <i class="fas fa-chevron-down ow-accordion-arrow"></i>
        </div>
        <div class="ow-accordion-body">${bodyHtml}</div>`;
    const header = wrapper.querySelector('.ow-accordion-header');
    const body   = wrapper.querySelector('.ow-accordion-body');
    header.addEventListener('click', () => {
        const collapsed = header.classList.toggle('collapsed');
        body.style.display = collapsed ? 'none' : '';
    });
    return wrapper;
}

// ── Edit modal launcher ───────────────────────────────────────────────────

export function openEditModal(config) {
    // config: { type, data, onSave }
    // type: 'epic'|'feature'|'story'|'task'|'testcase'
    const modal = document.getElementById('wizardEditModal');
    if (!modal) return;
    const bsModal = bootstrap.Modal.getOrCreateInstance(modal);

    const { type, data, onSave } = config;
    const title  = { epic: 'Edit Epic', feature: 'Edit Feature', story: 'Edit User Story',
                     task: 'Edit Task', testcase: 'Edit Test Case' }[type] ?? 'Edit';
    modal.querySelector('#wizardEditModalLabel').textContent = title;

    // Field group IDs that exist in the modal
    const allGroups = ['edit-desc-group','edit-ac-group','edit-priority-group',
                       'edit-opt-hours-group','edit-ml-hours-group','edit-pess-hours-group',
                       'edit-tc-steps-group','edit-tc-result-group'];

    const showGroups = (...ids) => ids.forEach(id => {
        const el = modal.querySelector(`#${id}`); if (el) el.style.display = '';
    });
    const hideGroups = (...ids) => ids.forEach(id => {
        const el = modal.querySelector(`#${id}`); if (el) el.style.display = 'none';
    });

    // Start: hide all optional groups
    hideGroups(...allGroups.filter(g => g !== 'edit-desc-group'));
    showGroups('edit-desc-group');

    if (type === 'epic' || type === 'feature') {
        modal.querySelector('#lbl-edit-title').textContent = type === 'epic' ? 'Epic Name' : 'Feature Name';
        modal.querySelector('#edit-title').value = data.name ?? '';
        modal.querySelector('#edit-desc').value  = data.description ?? '';
    }
    if (type === 'story') {
        modal.querySelector('#lbl-edit-title').textContent = 'Story Title';
        modal.querySelector('#edit-title').value = data.title ?? '';
        modal.querySelector('#edit-desc').value  = data.description ?? '';
        showGroups('edit-ac-group', 'edit-priority-group');
        modal.querySelector('#edit-ac').value = data.acceptanceCriteria ?? '';
        const sel = modal.querySelector('#edit-priority');
        if (sel) sel.value = data.priority ?? 'Medium';
    }
    if (type === 'task') {
        modal.querySelector('#lbl-edit-title').textContent = 'Task Title';
        modal.querySelector('#edit-title').value = data.title ?? '';
        modal.querySelector('#edit-desc').value  = data.description ?? '';
        showGroups('edit-priority-group','edit-opt-hours-group','edit-ml-hours-group','edit-pess-hours-group');
        const sel = modal.querySelector('#edit-priority');
        if (sel) sel.value = data.priority ?? 'Medium';
        modal.querySelector('#edit-opt-hours').value  = data.optimisticHours  ?? 0;
        modal.querySelector('#edit-ml-hours').value   = data.mostLikelyHours  ?? 0;
        modal.querySelector('#edit-pess-hours').value = data.pessimisticHours ?? 0;
    }
    if (type === 'testcase') {
        modal.querySelector('#lbl-edit-title').textContent = 'Test Case Title';
        modal.querySelector('#edit-title').value = data.title ?? '';
        hideGroups('edit-desc-group');
        showGroups('edit-tc-steps-group','edit-tc-result-group');
        modal.querySelector('#edit-tc-steps').value  = data.steps ?? '';
        modal.querySelector('#edit-tc-result').value = data.expectedResult ?? '';
    }

    // Save handler (replaced each time to avoid stale closures)
    const saveBtn = modal.querySelector('#btn-save-edit-item');
    const newBtn  = saveBtn.cloneNode(true);
    saveBtn.replaceWith(newBtn);
    newBtn.addEventListener('click', () => {
        const updated = { ...data };
        if (type === 'epic' || type === 'feature') {
            updated.name        = modal.querySelector('#edit-title').value.trim();
            updated.description = modal.querySelector('#edit-desc').value.trim();
        }
        if (type === 'story') {
            updated.title              = modal.querySelector('#edit-title').value.trim();
            updated.description        = modal.querySelector('#edit-desc').value.trim();
            updated.acceptanceCriteria = modal.querySelector('#edit-ac').value.trim();
            updated.priority           = modal.querySelector('#edit-priority').value;
        }
        if (type === 'task') {
            updated.title            = modal.querySelector('#edit-title').value.trim();
            updated.description      = modal.querySelector('#edit-desc').value.trim();
            updated.priority         = modal.querySelector('#edit-priority').value;
            updated.optimisticHours  = parseFloat(modal.querySelector('#edit-opt-hours').value)  || 0;
            updated.mostLikelyHours  = parseFloat(modal.querySelector('#edit-ml-hours').value)   || 0;
            updated.pessimisticHours = parseFloat(modal.querySelector('#edit-pess-hours').value) || 0;
        }
        if (type === 'testcase') {
            updated.title          = modal.querySelector('#edit-title').value.trim();
            updated.steps          = modal.querySelector('#edit-tc-steps').value.trim();
            updated.expectedResult = modal.querySelector('#edit-tc-result').value.trim();
        }
        onSave(updated);
        bsModal.hide();
    });

    bsModal.show();
}


// ── Tiny utilities ────────────────────────────────────────────────────────

function esc(str) {
    return String(str ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

export function calcPert(o, m, p) {
    return Math.round(((Number(o) + 4 * Number(m) + Number(p)) / 6) * 10) / 10;
}
