/**
 * wizard-step-3-features.js — Feature discovery (Step 3)
 *
 * KEY UX CHANGE: Analyzes ALL selected epics in PARALLEL using Promise.allSettled().
 * Shows a live progress grid so the user sees per-epic loading in real time.
 * Individual epic results appear as they complete — user doesn't wait for all.
 */
import { WizardState, apiFetch } from './wizard-state.js';
import { createEpicSlot, markEpicSlotDone, markEpicSlotError,
         renderFeatureItem, openEditModal, updateSummaryBar, skeletonRows } from './wizard-ui.js';

const grid     = () => document.getElementById('features-parallel-grid');
const listArea = () => document.getElementById('features-list-area');

export async function runStep3() {
    const selectedEpics = WizardState.epics.filter(e => e.selected !== false);

    const g = grid();
    const l = listArea();
    if (!g || !l) return;
    g.innerHTML = ''; l.innerHTML = '';

    if (selectedEpics.length === 0) {
        l.innerHTML = '<div class="alert alert-warning">No epics selected. Go back and select at least one epic.</div>';
        return;
    }

    // 1. Show a slot card for every epic BEFORE any fetch starts
    selectedEpics.forEach(epic => {
        // Only create slot if epic has a DB id (was saved in step 2)
        if (epic.id) g.appendChild(createEpicSlot(epic));
    });

    // 2. Fire ALL analyze calls concurrently
    const tasks = selectedEpics
        .filter(e => e.id)
        .map(epic => analyzeEpicFeatures(epic, l));

    await Promise.allSettled(tasks);

    // 3. After all complete, render consolidated review list
    renderAllFeatures(l);
    updateSummaryBar();
}

async function analyzeEpicFeatures(epic, listEl) {
    // Check if features already exist from checkpoint
    if (epic.features && epic.features.length > 0) {
        markEpicSlotDone(epic.id, epic.features.length);
        return;
    }

    try {
        const data = await apiFetch(`/api/onboard/analyze-features/${epic.id}`, { method: 'POST' });
        const features = (data.features ?? []).map(f => ({
            id: f.id, name: f.name, description: f.description,
            selected: true, userStories: []
        }));
        WizardState.setEpicFeatures(epic.id, features);
        markEpicSlotDone(epic.id, features.length);
    } catch (err) {
        markEpicSlotError(epic.id, err.message);
        WizardState.setEpicFeatures(epic.id, []);
    }
}

function renderAllFeatures(listEl) {
    listEl.innerHTML = '';

    WizardState.epics.forEach((epic, eIdx) => {
        if (!epic.selected || !epic.features?.length) return;

        // Epic group header
        const header = document.createElement('h6');
        header.style.cssText = 'font-weight:700;color:var(--primary-color);margin:1.25rem 0 .5rem;display:flex;align-items:center;gap:.5rem';
        header.innerHTML = `<i class="fas fa-layer-group"></i> ${epic.name}
            <span style="font-size:.75rem;font-weight:400;color:var(--text-secondary)">${epic.features.filter(f=>f.selected).length} features</span>`;
        listEl.appendChild(header);

        epic.features.forEach((feat, fIdx) => {
            listEl.appendChild(renderFeatureItem(feat, eIdx, fIdx, {
                onEdit:   (ei, fi) => editFeature(ei, fi),
                onDelete: (ei, fi) => { epic.features.splice(fi, 1); renderAllFeatures(listEl); }
            }));
        });

        // Add custom feature button for this epic
        const addBtn = document.createElement('button');
        addBtn.className = 'floating-add-btn';
        addBtn.innerHTML = `<i class="fas fa-plus"></i> Add feature to "${epic.name}"`;
        addBtn.addEventListener('click', () => {
            const newFeat = { id: null, name: 'New Feature', description: '', selected: true, userStories: [] };
            epic.features.push(newFeat);
            editFeature(eIdx, epic.features.length - 1);
            renderAllFeatures(listEl);
        });
        listEl.appendChild(addBtn);
    });

    // Skip step hint
    const hint = document.createElement('p');
    hint.className = 'text-muted small mt-3';
    hint.innerHTML = '<i class="fas fa-info-circle"></i> Uncheck features you don\'t need, or <button class="btn-ow-skip" id="btn-skip-step3">Skip this step</button> to add features manually.';
    listEl.appendChild(hint);
    document.getElementById('btn-skip-step3')?.addEventListener('click', () => {
        WizardState.epics.forEach(e => e.features = []);
        WizardState.emit('step:skip', 3);
    });
}

function editFeature(epicIdx, featIdx) {
    const feat = WizardState.epics[epicIdx].features[featIdx];
    openEditModal({
        type: 'feature', data: feat,
        onSave: updated => {
            WizardState.epics[epicIdx].features[featIdx] = { ...feat, name: updated.name, description: updated.description };
            renderAllFeatures(listArea());
        }
    });
}

export async function saveStep3() {
    for (const epic of WizardState.epics.filter(e => e.selected && e.id && e.features?.length)) {
        const selected = epic.features.filter(f => f.selected !== false);
        const saved = await apiFetch('/api/onboard/save-features', {
            method: 'POST',
            body: JSON.stringify({ epicId: epic.id, features: selected.map(f => ({ id: f.id, name: f.name, description: f.description })) })
        });
        WizardState.setEpicFeatures(epic.id, saved.map((s, i) => ({ ...selected[i], id: s.id })));
    }
    updateSummaryBar();
}
