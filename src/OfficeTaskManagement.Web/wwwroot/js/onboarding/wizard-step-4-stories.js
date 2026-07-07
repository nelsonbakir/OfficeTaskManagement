/**
 * wizard-step-4-stories.js — User Story discovery (Step 4)
 */
import { WizardState, apiFetch } from './wizard-state.js';
import { renderStoryItem, openEditModal, updateSummaryBar,
         createEpicSlot, markEpicSlotDone, markEpicSlotError } from './wizard-ui.js';

const grid     = () => document.getElementById('stories-parallel-grid');
const listArea = () => document.getElementById('stories-list-area');

let _originalStoriesBackup = null;

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

    const allHaveStories = allFeatures.every(f => f.feat.userStories && f.feat.userStories.length > 0);
    if (allHaveStories) {
        renderAllStories(l, false);
        updateSummaryBar();
        return;
    }

    await fetchStories(g, l, allFeatures);
}

async function fetchStories(g, l, allFeatures, forceRegen = false, isDraft = false) {
    g.innerHTML = ''; l.innerHTML = '';

    // Create slots for each feature (using epic slot component, labelled by feature name)
    allFeatures.forEach(({ feat }) => {
        const slotEl = document.createElement('div');
        slotEl.className = 'ow-epic-slot loading';
        slotEl.dataset.epicId = feat.id;
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

    WizardState.setAnalyzing(true);
    try {
        // Fire all in parallel
        await Promise.allSettled(allFeatures.map(entry => analyzeFeatureStories(entry, forceRegen, isDraft)));
    } finally {
        WizardState.setAnalyzing(false);
    }

    renderAllStories(l, isDraft);
    updateSummaryBar();
}

async function analyzeFeatureStories({ epic, epicIdx, feat, featIdx }, forceRegen = false, isDraft = false) {
    // Already loaded from checkpoint?
    if (!forceRegen && feat.userStories && feat.userStories.length > 0) {
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
        
        if (isDraft) {
            feat.draftStories = stories;
            markEpicSlotDone(feat.id, stories.length);
        } else {
            WizardState.setFeatureStories(feat.id, stories);
            markEpicSlotDone(feat.id, stories.length);
        }
    } catch (err) {
        markEpicSlotError(feat.id, err.message);
        if (isDraft) {
            feat.draftStories = [];
        } else {
            WizardState.setFeatureStories(feat.id, []);
        }
    }
}

function renderAllStories(listEl, isDraft = false) {
    listEl.innerHTML = '';

    if (isDraft) {
        const banner = document.createElement('div');
        banner.className = 'alert alert-warning d-flex align-items-center justify-content-between mb-3';
        banner.style.borderRadius = 'var(--ow-radius)';
        banner.innerHTML = `
            <div>
                <strong style="color:#d97706"><i class="fas fa-magic"></i> AI Suggested Draft User Stories</strong>
                <p class="mb-0 text-muted small" style="margin-top:0.25rem">Review the suggestions below. You can edit them before accepting, or discard them to keep your original stories.</p>
            </div>
            <div class="d-flex gap-2 ms-3">
                <button class="btn btn-sm btn-success text-white" id="btn-accept-regen-stories">Accept &amp; Replace</button>
                <button class="btn btn-sm btn-outline-secondary" id="btn-cancel-regen-stories">Keep Original</button>
            </div>`;
        listEl.appendChild(banner);

        setTimeout(() => {
            document.getElementById('btn-accept-regen-stories')?.addEventListener('click', () => {
                WizardState.epics.forEach(epic => {
                    (epic.features ?? []).forEach(feat => {
                        if (feat.draftStories) {
                            feat.userStories = feat.draftStories;
                            delete feat.draftStories;
                        }
                    });
                });
                _originalStoriesBackup = null;
                renderAllStories(listEl, false);
            });
            document.getElementById('btn-cancel-regen-stories')?.addEventListener('click', () => {
                _originalStoriesBackup.forEach(backup => {
                    let found = null;
                    WizardState.epics.forEach(epic => {
                        const feat = (epic.features ?? []).find(f => f.id === backup.featureId);
                        if (feat) found = feat;
                    });
                    if (found) {
                        found.userStories = backup.stories;
                        delete found.draftStories;
                    }
                });
                _originalStoriesBackup = null;
                renderAllStories(listEl, false);
            });
        }, 50);
    }

    WizardState.epics.forEach((epic, eIdx) => {
        if (!epic.selected) return;
        (epic.features ?? []).forEach((feat, fIdx) => {
            if (!feat.selected) return;
            const stories = isDraft ? (feat.draftStories ?? []) : (feat.userStories ?? []);

            const groupHeader = document.createElement('h6');
            groupHeader.style.cssText = 'font-weight:700;color:#6264A7;margin:1.25rem 0 .5rem;display:flex;align-items:center;gap:.5rem';
            groupHeader.innerHTML = `<i class="fas fa-puzzle-piece"></i> ${feat.name}
                <span style="font-size:.75rem;font-weight:400;color:var(--text-secondary)">${stories.filter(s=>s.selected).length} stories</span>`;
            listEl.appendChild(groupHeader);

            stories.forEach((story, sIdx) => {
                listEl.appendChild(renderStoryItem(story, eIdx, fIdx, sIdx, {
                    onEdit:   (ei, fi, si) => editStory(story, stories, isDraft),
                    onDelete: (ei, fi, si) => { stories.splice(si, 1); renderAllStories(listEl, isDraft); }
                }));
            });

            if (!isDraft) {
                // Add custom story
                const addBtn = document.createElement('button');
                addBtn.className = 'floating-add-btn';
                addBtn.innerHTML = `<i class="fas fa-plus"></i> Add story to "${feat.name}"`;
                addBtn.addEventListener('click', () => {
                    const ns = { id: null, title: 'New Story', description: '', acceptanceCriteria: '', priority: 'Medium', selected: true, tasks: [], testCases: [] };
                    (feat.userStories ??= []).push(ns);
                    editStory(ns, feat.userStories, isDraft);
                });
                listEl.appendChild(addBtn);
            }
        });
    });

    if (isDraft) return;

    // Regenerate Stories button
    const regenBtn = document.createElement('button');
    regenBtn.className = 'floating-regen-btn mt-3 mb-2';
    regenBtn.innerHTML = '<i class="fas fa-redo"></i> Regenerate User Stories';
    regenBtn.addEventListener('click', async () => {
        if (!confirm("Are you sure you want to regenerate User Stories? Any custom changes or selections for this step will be lost.")) return;
        
        _originalStoriesBackup = [];
        WizardState.epics.forEach(e => {
            (e.features ?? []).forEach(f => {
                if (f.id) {
                    _originalStoriesBackup.push({
                        featureId: f.id,
                        stories: JSON.parse(JSON.stringify(f.userStories ?? []))
                    });
                }
            });
        });

        const allFeatures = [];
        WizardState.epics.forEach((epic, ei) => {
            if (!epic.selected) return;
            (epic.features ?? []).forEach((feat, fi) => {
                if (!feat.selected || !feat.id) return;
                allFeatures.push({ epic, epicIdx: ei, feat, featIdx: fi });
            });
        });
        
        const g = grid();
        await fetchStories(g, listEl, allFeatures, true, true);
    });
    listEl.appendChild(regenBtn);

    // Skip hint
    const hint = document.createElement('p');
    hint.className = 'text-muted small mt-2';
    hint.innerHTML = '<i class="fas fa-info-circle"></i> Uncheck stories to exclude them. <button class="btn-ow-skip" id="btn-skip-step4">Skip this step</button>';
    listEl.appendChild(hint);
    document.getElementById('btn-skip-step4')?.addEventListener('click', () => {
        WizardState.epics.forEach(e => (e.features ?? []).forEach(f => f.userStories = []));
        WizardState.emit('step:skip', 4);
    });
}

function editStory(story, storiesList, isDraft) {
    openEditModal({
        type: 'story', data: story,
        onSave: updated => {
            Object.assign(story, updated);
            renderAllStories(listArea(), isDraft);
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
