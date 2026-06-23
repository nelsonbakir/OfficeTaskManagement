/**
 * wizard-step-2-epics.js — Epic review & confirmation (Step 2)
 * Calls AI analysis, then lets user review/edit individual epics.
 * No "Accept All" — every item must be reviewed (per product decision).
 */
import { WizardState, apiFetch } from './wizard-state.js';
import { renderEpicItem, skeletonRows, openEditModal, statusBadgeHtml, updateSummaryBar } from './wizard-ui.js';

const container = () => document.getElementById('epics-list-container');
const overviewCard = () => document.getElementById('overview-card');

export async function runStep2() {
    const el = container();
    if (!el) return;

    // If epics already loaded from checkpoint, just render them
    if (WizardState.epics.length > 0) {
        renderEpics(WizardState.epics);
        showOverviewCard();
        return;
    }

    el.innerHTML = skeletonRows(4);

    try {
        const analysis = await apiFetch(`/api/onboard/analyze-project/${WizardState.projectId}`, { method: 'POST' });
        WizardState.setAnalysisResult(analysis);

        const epics = (analysis.suggestedEpics ?? []).map(e => ({
            id: null, name: e.name, description: e.description, selected: true, features: []
        }));
        WizardState.setEpics(epics);

        showOverviewCard();
        renderEpics(epics);
    } catch (err) {
        el.innerHTML = `<div class="alert alert-danger"><i class="fas fa-exclamation-triangle"></i> AI analysis failed: ${err.message}. <button class="btn btn-sm btn-outline-danger ms-2" id="btn-retry-epics">Retry</button></div>`;
        document.getElementById('btn-retry-epics')?.addEventListener('click', runStep2);
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

function renderEpics(epics) {
    const el = container();
    if (!el) return;
    el.innerHTML = '';

    epics.forEach((epic, idx) => {
        el.appendChild(renderEpicItem(epic, idx, {
            onEdit:   (i)    => editEpic(i),
            onDelete: (i)    => removeEpic(i, el)
        }));
    });

    // "Add custom epic" button
    const addBtn = document.createElement('button');
    addBtn.className = 'floating-add-btn';
    addBtn.innerHTML = '<i class="fas fa-plus"></i> Add Custom Epic';
    addBtn.addEventListener('click', () => {
        const newEpic = { id: null, name: 'New Epic', description: '', selected: true, features: [] };
        WizardState.epics.push(newEpic);
        editEpic(WizardState.epics.length - 1, true);
        renderEpics(WizardState.epics);
    });
    el.appendChild(addBtn);

    // Skip-all link
    if (!el.nextElementSibling?.classList?.contains('ow-skip-hint')) {
        const hint = document.createElement('p');
        hint.className = 'ow-skip-hint text-muted small mt-2';
        hint.innerHTML = '<i class="fas fa-info-circle"></i> Uncheck epics you don\'t want to include. You can also skip this step and add epics manually later.';
        el.after(hint);
    }
}

function editEpic(idx, isNew = false) {
    const epic = WizardState.epics[idx];
    openEditModal({
        type: 'epic', data: epic,
        onSave: updated => {
            WizardState.epics[idx] = { ...epic, name: updated.name, description: updated.description };
            renderEpics(WizardState.epics);
        }
    });
}

function removeEpic(idx, el) {
    WizardState.epics.splice(idx, 1);
    renderEpics(WizardState.epics);
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
