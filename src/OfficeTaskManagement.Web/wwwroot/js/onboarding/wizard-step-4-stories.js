/**
 * wizard-step-4-stories.js — User Story discovery (Step 4)
 * Parallel analysis per feature across all selected epics.
 */
import { WizardState, apiFetch } from './wizard-state.js';
import { renderStoryItem, openEditModal, updateSummaryBar, skeletonRows,
         createEpicSlot, markEpicSlotDone, markEpicSlotError } from './wizard-ui.js';

const grid     = () => document.getElementById('stories-parallel-grid');
const listArea = () => document.getElementById('stories-list-area');

export async function runStep4() {
    const g = grid(); const l = listArea();
    if (!g || !l) return;
    g.innerHTML = ''; l.innerHTML = '';

    // Collect all selected features across all epics
    const allFeatures = [];
    WizardState.epics.forEach((epic, ei) => {
        if (!epic.selected) return;
        (epic.features ?? []).forEach((feat, fi) => {
            if (!feat.selected || !feat.id) return;
            allFeatures.push({ epic, epicIdx: ei, feat, featIdx: fi });
        });
    });

    if (allFeatures.length === 0) {
        l.innerHTML = '<div class="alert alert-warning">No features available. Go back to Step 3 or add features manually.</div>';
        return;
    }

    // Create slots for each feature (using epic slot component, labelled by feature name)
    allFeatures.forEach(({ feat }) => {
        const slotEl = document.createElement('div');
        slotEl.className = 'ow-epic-slot loading';
        slotEl.dataset.epicId = feat.id;   // reuse epicId key for slot targeting
        slotEl.innerHTML = `
            <div class="ow-epic-slot-header">
                <h5 title="${feat.name}">${feat.name}</h5>
                <span class="ow-status-badge ai-loading"><span class="ow-badge-dot"></span>Analyzing</span>
            </div>
            <div class="ow-epic-slot-body">
                <div class="ow-slot-progress"><div class="ow-slot-progress-fill"></div></div>
                <div class="ow-slot-loading-text">Discovering user stories…</div>
            </div>`;
        g.appendChild(slotEl);
    });

    // Fire all in parallel
    await Promise.allSettled(allFeatures.map(entry => analyzeFeatureStories(entry)));

    renderAllStories(l);
    updateSummaryBar();
}

async function analyzeFeatureStories({ epic, epicIdx, feat, featIdx }) {
    // Already loaded from checkpoint?
    if (feat.userStories && feat.userStories.length > 0) {
        markEpicSlotDone(feat.id, feat.userStories.length);
        return;
    }

    try {
        const data = await apiFetch(`/api/onboard/analyze-stories/${feat.id}`, { method: 'POST' });
        const stories = (data.stories ?? []).map(s => ({
            id: s.id, title: s.title, description: s.description,
            acceptanceCriteria: s.acceptanceCriteria, priority: s.priority,
            selected: true, tasks: [], testCases: []
        }));
        WizardState.setFeatureStories(feat.id, stories);
        markEpicSlotDone(feat.id, stories.length);
    } catch (err) {
        markEpicSlotError(feat.id, err.message);
        WizardState.setFeatureStories(feat.id, []);
    }
}

function renderAllStories(listEl) {
    listEl.innerHTML = '';

    WizardState.epics.forEach((epic, eIdx) => {
        if (!epic.selected) return;
        (epic.features ?? []).forEach((feat, fIdx) => {
            if (!feat.selected) return;
            const stories = feat.userStories ?? [];

            const groupHeader = document.createElement('h6');
            groupHeader.style.cssText = 'font-weight:700;color:#6264A7;margin:1.25rem 0 .5rem;display:flex;align-items:center;gap:.5rem';
            groupHeader.innerHTML = `<i class="fas fa-puzzle-piece"></i> ${feat.name}
                <span style="font-size:.75rem;font-weight:400;color:var(--text-secondary)">${stories.filter(s=>s.selected).length} stories</span>`;
            listEl.appendChild(groupHeader);

            stories.forEach((story, sIdx) => {
                listEl.appendChild(renderStoryItem(story, eIdx, fIdx, sIdx, {
                    onEdit:   (ei, fi, si) => editStory(ei, fi, si),
                    onDelete: (ei, fi, si) => { feat.userStories.splice(si, 1); renderAllStories(listEl); }
                }));
            });

            // Add custom story
            const addBtn = document.createElement('button');
            addBtn.className = 'floating-add-btn';
            addBtn.innerHTML = `<i class="fas fa-plus"></i> Add story to "${feat.name}"`;
            addBtn.addEventListener('click', () => {
                const ns = { id: null, title: 'New Story', description: '', acceptanceCriteria: '', priority: 'Medium', selected: true, tasks: [], testCases: [] };
                (feat.userStories ??= []).push(ns);
                editStory(eIdx, fIdx, feat.userStories.length - 1);
                renderAllStories(listEl);
            });
            listEl.appendChild(addBtn);
        });
    });

    // Skip hint
    const hint = document.createElement('p');
    hint.className = 'text-muted small mt-3';
    hint.innerHTML = '<i class="fas fa-info-circle"></i> Uncheck stories to exclude them. <button class="btn-ow-skip" id="btn-skip-step4">Skip this step</button>';
    listEl.appendChild(hint);
    document.getElementById('btn-skip-step4')?.addEventListener('click', () => {
        WizardState.epics.forEach(e => (e.features ?? []).forEach(f => f.userStories = []));
        WizardState.emit('step:skip', 4);
    });
}

function editStory(epicIdx, featIdx, storyIdx) {
    const story = WizardState.epics[epicIdx].features[featIdx].userStories[storyIdx];
    openEditModal({
        type: 'story', data: story,
        onSave: updated => {
            Object.assign(story, updated);
            renderAllStories(listArea());
        }
    });
}

export async function saveStep4() {
    for (const epic of WizardState.epics.filter(e => e.selected)) {
        for (const feat of (epic.features ?? []).filter(f => f.selected && f.id)) {
            const selected = (feat.userStories ?? []).filter(s => s.selected !== false);
            if (!selected.length) continue;
            const saved = await apiFetch('/api/onboard/save-stories', {
                method: 'POST',
                body: JSON.stringify({ featureId: feat.id, stories: selected.map(s => ({
                    id: s.id, title: s.title, description: s.description,
                    acceptanceCriteria: s.acceptanceCriteria, priority: s.priority
                })) })
            });
            WizardState.setFeatureStories(feat.id, saved.map((s, i) => ({ ...selected[i], id: s.id })));
        }
    }
    updateSummaryBar();
}
