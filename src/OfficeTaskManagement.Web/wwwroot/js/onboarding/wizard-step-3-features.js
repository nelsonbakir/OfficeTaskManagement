/**
 * wizard-step-3-features.js — Feature discovery (Step 3)
 */
import { WizardState, apiFetch } from './wizard-state.js';
import { createEpicSlot, markEpicSlotDone, markEpicSlotError,
         renderFeatureItem, openEditModal, updateSummaryBar } from './wizard-ui.js';

const grid     = () => document.getElementById('features-parallel-grid');
const listArea = () => document.getElementById('features-list-area');

let _originalFeaturesBackup = null;

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

    const allHaveFeatures = selectedEpics.every(e => e.features && e.features.length > 0);
    if (allHaveFeatures) {
        renderAllFeatures(l, false);
        updateSummaryBar();
        return;
    }

    await fetchFeatures(g, l, selectedEpics);
}

async function fetchFeatures(g, l, selectedEpics, forceRegen = false, isDraft = false) {
    g.innerHTML = ''; l.innerHTML = '';

    // 1. Show a slot card for every epic BEFORE any fetch starts
    selectedEpics.forEach(epic => {
        if (epic.id) g.appendChild(createEpicSlot(epic));
    });

    WizardState.setAnalyzing(true);
    try {
        // 2. Fire ALL analyze calls concurrently
        const tasks = selectedEpics
            .filter(e => e.id)
            .map(epic => analyzeEpicFeatures(epic, l, forceRegen, isDraft));

        await Promise.allSettled(tasks);
    } finally {
        WizardState.setAnalyzing(false);
    }

    // 3. After all complete, render consolidated review list
    renderAllFeatures(l, isDraft);
    updateSummaryBar();
}

async function analyzeEpicFeatures(epic, listEl, forceRegen = false, isDraft = false) {
    // Check if features already exist from checkpoint
    if (!forceRegen && epic.features && epic.features.length > 0) {
        markEpicSlotDone(epic.id, epic.features.length);
        return;
    }

    try {
        const data = await apiFetch(`/api/onboard/analyze-features/${epic.id}`, { method: 'POST' });
        const features = (data.features ?? []).map(f => ({
            id: f.id, name: f.name, description: f.description,
            selected: true, userStories: []
        }));
        
        if (isDraft) {
            epic.draftFeatures = features;
            markEpicSlotDone(epic.id, features.length);
        } else {
            WizardState.setEpicFeatures(epic.id, features);
            markEpicSlotDone(epic.id, features.length);
        }
    } catch (err) {
        markEpicSlotError(epic.id, err.message);
        if (isDraft) {
            epic.draftFeatures = [];
        } else {
            WizardState.setEpicFeatures(epic.id, []);
        }
    }
}

function renderAllFeatures(listEl, isDraft = false) {
    listEl.innerHTML = '';

    if (isDraft) {
        const banner = document.createElement('div');
        banner.className = 'alert alert-warning d-flex align-items-center justify-content-between mb-3';
        banner.style.borderRadius = 'var(--ow-radius)';
        banner.innerHTML = `
            <div>
                <strong style="color:#d97706"><i class="fas fa-magic"></i> AI Suggested Draft Features</strong>
                <p class="mb-0 text-muted small" style="margin-top:0.25rem">Review the suggestions below. You can edit them before accepting, or discard them to keep your original features.</p>
            </div>
            <div class="d-flex gap-2 ms-3">
                <button class="btn btn-sm btn-success text-white" id="btn-accept-regen-features">Accept &amp; Replace</button>
                <button class="btn btn-sm btn-outline-secondary" id="btn-cancel-regen-features">Keep Original</button>
            </div>`;
        listEl.appendChild(banner);

        setTimeout(() => {
            document.getElementById('btn-accept-regen-features')?.addEventListener('click', () => {
                WizardState.epics.forEach(epic => {
                    if (epic.draftFeatures) {
                        epic.features = epic.draftFeatures;
                        delete epic.draftFeatures;
                    }
                });
                _originalFeaturesBackup = null;
                renderAllFeatures(listEl, false);
            });
            document.getElementById('btn-cancel-regen-features')?.addEventListener('click', () => {
                _originalFeaturesBackup.forEach(backup => {
                    const epic = WizardState.epics.find(e => e.id === backup.epicId);
                    if (epic) {
                        epic.features = backup.features;
                        delete epic.draftFeatures;
                    }
                });
                _originalFeaturesBackup = null;
                renderAllFeatures(listEl, false);
            });
        }, 50);
    }

    WizardState.epics.forEach((epic, eIdx) => {
        if (!epic.selected) return;
        const features = isDraft ? (epic.draftFeatures ?? []) : (epic.features ?? []);
        if (!features.length) return;

        // Epic group header
        const header = document.createElement('h6');
        header.style.cssText = 'font-weight:700;color:var(--primary-color);margin:1.25rem 0 .5rem;display:flex;align-items:center;gap:.5rem';
        header.innerHTML = `<i class="fas fa-layer-group"></i> ${epic.name}
            <span style="font-size:.75rem;font-weight:400;color:var(--text-secondary)">${features.filter(f=>f.selected).length} features</span>`;
        listEl.appendChild(header);

        features.forEach((feat, fi) => {
            listEl.appendChild(renderFeatureItem(feat, eIdx, fi, {
                onEdit:   (ei, fIdx) => editFeature(feat, features, isDraft),
                onDelete: (ei, fIdx) => { features.splice(fIdx, 1); renderAllFeatures(listEl, isDraft); }
            }));
        });

        if (!isDraft) {
            // Add custom feature button for this epic
            const addBtn = document.createElement('button');
            addBtn.className = 'floating-add-btn';
            addBtn.innerHTML = `<i class="fas fa-plus"></i> Add feature to "${epic.name}"`;
            addBtn.addEventListener('click', () => {
                const newFeat = { id: null, name: 'New Feature', description: '', selected: true, userStories: [] };
                epic.features.push(newFeat);
                editFeature(newFeat, epic.features, isDraft);
            });
            listEl.appendChild(addBtn);
        }
    });

    if (isDraft) return;

    // Regenerate Features button
    const regenBtn = document.createElement('button');
    regenBtn.className = 'floating-regen-btn mt-3 mb-2';
    regenBtn.innerHTML = '<i class="fas fa-redo"></i> Regenerate Features';
    regenBtn.addEventListener('click', async () => {
        if (!confirm("Are you sure you want to regenerate Features? Any custom changes or selections for this step will be lost.")) return;
        _originalFeaturesBackup = WizardState.epics.map(e => ({
            epicId: e.id,
            features: JSON.parse(JSON.stringify(e.features ?? []))
        }));
        
        const selectedEpics = WizardState.epics.filter(e => e.selected !== false);
        selectedEpics.forEach(e => e.features = []);
        const g = grid();
        await fetchFeatures(g, listEl, selectedEpics, true, true);
    });
    listEl.appendChild(regenBtn);

    // Skip step hint
    const hint = document.createElement('p');
    hint.className = 'text-muted small mt-2';
    hint.innerHTML = '<i class="fas fa-info-circle"></i> Uncheck features you don\'t need, or <button class="btn-ow-skip" id="btn-skip-step3">Skip this step</button> to add features manually.';
    listEl.appendChild(hint);
    document.getElementById('btn-skip-step3')?.addEventListener('click', () => {
        WizardState.epics.forEach(e => e.features = []);
        WizardState.emit('step:skip', 3);
    });
}

function editFeature(feat, featuresList, isDraft) {
    openEditModal({
        type: 'feature', data: feat,
        onSave: updated => {
            feat.name = updated.name;
            feat.description = updated.description;
            renderAllFeatures(listArea(), isDraft);
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
