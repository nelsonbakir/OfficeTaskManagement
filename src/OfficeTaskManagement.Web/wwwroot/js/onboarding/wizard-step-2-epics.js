/**
 * wizard-step-2-epics.js — Epic review & confirmation (Step 2)
 * Calls AI analysis, then lets user review/edit individual epics.
 */
import { WizardState, apiFetch } from './wizard-state.js';
import { renderEpicItem, skeletonRows, openEditModal, updateSummaryBar } from './wizard-ui.js';

const container = () => document.getElementById('epics-list-container');
const overviewCard = () => document.getElementById('overview-card');

let _originalEpicsBackup = null;
let _draftEpics = null;

export async function runStep2() {
    const el = container();
    if (!el) return;

    // If epics already loaded from checkpoint, just render them
    if (WizardState.epics.length > 0) {
        renderEpics(WizardState.epics, false);
        showOverviewCard();
        return;
    }

    await fetchEpics(el);
}

async function fetchEpics(el) {
    el.innerHTML = skeletonRows(4);

    try {
        const analysis = await apiFetch(`/api/onboard/analyze-project/${WizardState.projectId}`, { method: 'POST' });
        WizardState.setAnalysisResult(analysis);

        const epics = (analysis.suggestedEpics ?? []).map(e => ({
            id: null, name: e.name, description: e.description, selected: true, features: []
        }));
        
        if (_originalEpicsBackup !== null) {
            _draftEpics = epics;
            renderEpics(_draftEpics, true);
        } else {
            WizardState.setEpics(epics);
            showOverviewCard();
            renderEpics(epics, false);
        }
    } catch (err) {
        el.innerHTML = `<div class="alert alert-danger"><i class="fas fa-exclamation-triangle"></i> AI analysis failed: ${err.message}. <button class="btn btn-sm btn-outline-danger ms-2" id="btn-retry-epics">Retry</button></div>`;
        document.getElementById('btn-retry-epics')?.addEventListener('click', () => fetchEpics(el));
    }
}

export async function saveStep2() {
    const selected = WizardState.epics.filter(e => e.selected !== false);
    if (selected.length === 0) throw new Error('Select at least one epic before continuing.');

    const saved = await apiFetch('/api/onboard/save-epics', {
        method: 'POST',
        body: JSON.stringify({ projectId: WizardState.projectId, epics: selected.map(e => ({ id: e.id, name: e.name, description: e.description })) })
    });

    // Merge saved IDs back into state
    WizardState.setEpics(saved.map((s, i) => ({
        ...selected[i],
        id: s.id,
        name: s.name,
        description: s.description
    })));
    updateSummaryBar();
}

function renderEpics(epics, isDraft = false) {
    const el = container();
    if (!el) return;
    el.innerHTML = '';

    if (isDraft) {
        const banner = document.createElement('div');
        banner.className = 'alert alert-warning d-flex align-items-center justify-content-between mb-3';
        banner.style.borderRadius = 'var(--ow-radius)';
        banner.innerHTML = `
            <div>
                <strong style="color:#d97706"><i class="fas fa-magic"></i> AI Suggested Draft Epics</strong>
                <p class="mb-0 text-muted small" style="margin-top:0.25rem">Review the suggestions below. You can edit them before accepting, or discard them to keep your original epics.</p>
            </div>
            <div class="d-flex gap-2 ms-3">
                <button class="btn btn-sm btn-success text-white" id="btn-accept-regen-epics">Accept &amp; Replace</button>
                <button class="btn btn-sm btn-outline-secondary" id="btn-cancel-regen-epics">Keep Original</button>
            </div>`;
        el.appendChild(banner);

        setTimeout(() => {
            document.getElementById('btn-accept-regen-epics')?.addEventListener('click', () => {
                WizardState.setEpics(_draftEpics);
                _originalEpicsBackup = null;
                _draftEpics = null;
                renderEpics(WizardState.epics, false);
            });
            document.getElementById('btn-cancel-regen-epics')?.addEventListener('click', () => {
                WizardState.epics = _originalEpicsBackup;
                _originalEpicsBackup = null;
                _draftEpics = null;
                renderEpics(WizardState.epics, false);
            });
        }, 50);
    }

    epics.forEach((epic, idx) => {
        el.appendChild(renderEpicItem(epic, idx, {
            onEdit:   (i)    => editEpic(epic, epics, isDraft),
            onDelete: (i)    => { epics.splice(i, 1); renderEpics(epics, isDraft); }
        }));
    });

    if (isDraft) return;

    // Action buttons container for Add / Regenerate
    const btnRow = document.createElement('div');
    btnRow.className = 'row g-2 mt-2';

    const colAdd = document.createElement('div');
    colAdd.className = 'col-md-6';
    const addBtn = document.createElement('button');
    addBtn.className = 'floating-add-btn';
    addBtn.style.marginTop = '0';
    addBtn.innerHTML = '<i class="fas fa-plus"></i> Add Custom Epic';
    addBtn.addEventListener('click', () => {
        const newEpic = { id: null, name: 'New Epic', description: '', selected: true, features: [] };
        WizardState.epics.push(newEpic);
        editEpic(newEpic, WizardState.epics, false, true);
    });
    colAdd.appendChild(addBtn);
    btnRow.appendChild(colAdd);

    const colRegen = document.createElement('div');
    colRegen.className = 'col-md-6';
    const regenBtn = document.createElement('button');
    regenBtn.className = 'floating-regen-btn';
    regenBtn.style.marginTop = '0';
    regenBtn.innerHTML = '<i class="fas fa-redo"></i> Regenerate Epics';
    regenBtn.addEventListener('click', async () => {
        if (!confirm("Are you sure you want to regenerate Epics? Any custom changes or selections for this step will be lost.")) return;
        _originalEpicsBackup = JSON.parse(JSON.stringify(WizardState.epics));
        await fetchEpics(el);
    });
    colRegen.appendChild(regenBtn);
    btnRow.appendChild(colRegen);

    el.appendChild(btnRow);

    // Skip-all link
    if (!el.nextElementSibling?.classList?.contains('ow-skip-hint')) {
        const hint = document.createElement('p');
        hint.className = 'ow-skip-hint text-muted small mt-2';
        hint.innerHTML = '<i class="fas fa-info-circle"></i> Uncheck epics you don\'t want to include. You can also skip this step and add epics manually later.';
        el.after(hint);
    }
}

function editEpic(epic, epicsList, isDraft, isNew = false) {
    openEditModal({
        type: 'epic', data: epic,
        onSave: updated => {
            epic.name = updated.name;
            epic.description = updated.description;
            renderEpics(epicsList, isDraft);
        }
    });
}

function showOverviewCard() {
    const ar = WizardState.analysisResult;
    const card = overviewCard();
    if (!card || !ar.projectSummary) return;
    card.style.display = '';
    const techEl    = document.getElementById('onboard-badge-tech');
    const summEl    = document.getElementById('onboard-overview-summary');
    const covEl     = document.getElementById('onboard-badge-coverage');
    const testsEl   = document.getElementById('onboard-overview-tests');
    if (techEl)  techEl.textContent  = ar.techStack || 'N/A';
    if (summEl)  summEl.textContent  = ar.projectSummary;
    if (covEl)   { covEl.textContent = ar.testsAbsentOrIncomplete ? '⚠ Gaps Detected' : '✓ Comprehensive';
                   covEl.className   = `badge-coverage${ar.testsAbsentOrIncomplete ? '' : ' passed'}`; }
    if (testsEl) { testsEl.textContent = ar.testOverview;
                   testsEl.className  = `test-overview-desc ${ar.testsAbsentOrIncomplete ? 'warning' : 'passed'}`; }
}
