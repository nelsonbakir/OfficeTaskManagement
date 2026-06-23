/**
 * wizard-step-5-tasks.js — Task & Test Case review (Step 5)
 * Parallel analysis across all selected stories.
 * Shows paginated accordion: max 20 stories per page to prevent DOM explosion.
 */
import { WizardState, apiFetch } from './wizard-state.js';
import { renderTaskRow, renderTestCaseRow, openEditModal,
         calcPert, updateSummaryBar, skeletonRows } from './wizard-ui.js';

const grid     = () => document.getElementById('tasks-parallel-grid');
const listArea = () => document.getElementById('tasks-list-area');

const PAGE_SIZE = 20;
let _currentPage = 0;
let _allStories  = [];

export async function runStep5() {
    const g = grid(); const l = listArea();
    if (!g || !l) return;
    g.innerHTML = ''; l.innerHTML = '';

    // Flatten all selected stories
    _allStories = [];
    WizardState.epics.forEach((epic, ei) => {
        if (!epic.selected) return;
        (epic.features ?? []).forEach((feat, fi) => {
            if (!feat.selected) return;
            (feat.userStories ?? []).forEach((story, si) => {
                if (!story.selected || !story.id) return;
                _allStories.push({ epic, epicIdx: ei, feat, featIdx: fi, story, storyIdx: si });
            });
        });
    });

    if (_allStories.length === 0) {
        l.innerHTML = '<div class="alert alert-warning">No user stories to analyse. Go back to Step 4 or add stories manually.</div>';
        return;
    }

    // Create mini slot per story in the parallel grid
    _allStories.forEach(({ story }) => {
        const slot = document.createElement('div');
        slot.className = 'ow-epic-slot loading';
        slot.dataset.epicId = story.id;
        slot.innerHTML = `
            <div class="ow-epic-slot-header">
                <h5 title="${esc(story.title)}">${esc(story.title)}</h5>
                <span class="ow-status-badge ai-loading"><span class="ow-badge-dot"></span>Analyzing</span>
            </div>
            <div class="ow-epic-slot-body">
                <div class="ow-slot-progress"><div class="ow-slot-progress-fill"></div></div>
                <div class="ow-slot-loading-text">Generating tasks &amp; tests…</div>
            </div>`;
        g.appendChild(slot);
    });

    await Promise.allSettled(_allStories.map(entry => analyzeStoryTasks(entry)));

    renderPage(l, 0);
    updateSummaryBar();
}

async function analyzeStoryTasks({ story }) {
    const slot = document.querySelector(`.ow-epic-slot[data-epic-id="${story.id}"]`);

    // Already loaded from checkpoint?
    if ((story.tasks?.length > 0) || (story.testCases?.length > 0)) {
        markSlotDone(slot, story.tasks?.length ?? 0, story.testCases?.length ?? 0);
        return;
    }

    try {
        const data = await apiFetch(`/api/onboard/analyze-tasks-tests/${story.id}`, { method: 'POST' });
        const tasks     = (data.tasks     ?? []).map(t  => ({ ...t, selected: true }));
        const testCases = (data.testCases ?? []).map(tc => ({ ...tc, selected: true }));
        WizardState.setStoryTasksTests(story.id, tasks, testCases);
        markSlotDone(slot, tasks.length, testCases.length);
    } catch (err) {
        if (slot) {
            slot.classList.replace('loading', 'error');
            const badge = slot.querySelector('.ow-status-badge');
            if (badge) { badge.className = ''; badge.style.cssText = 'font-size:.72rem;font-weight:700;padding:.2rem .6rem;border-radius:20px;background:#FDE7E9;color:var(--danger-color)'; badge.textContent = '✗ Error'; }
            const txt = slot.querySelector('.ow-slot-loading-text');
            if (txt) txt.textContent = err.message;
        }
    }
}

function markSlotDone(slot, taskCount, testCount) {
    if (!slot) return;
    slot.classList.replace('loading', 'done');
    const badge = slot.querySelector('.ow-status-badge');
    if (badge) { badge.className = 'ow-status-badge saved'; badge.textContent = `✓ ${taskCount}T / ${testCount}TC`; }
    const fill = slot.querySelector('.ow-slot-progress-fill');
    if (fill) fill.style.width = '100%';
    const txt = slot.querySelector('.ow-slot-loading-text');
    if (txt) txt.textContent = `${taskCount} task${taskCount !== 1 ? 's' : ''}, ${testCount} test case${testCount !== 1 ? 's' : ''}`;
}

function renderPage(listEl, page) {
    _currentPage = page;
    listEl.innerHTML = '';

    const start = page * PAGE_SIZE;
    const pageSlice = _allStories.slice(start, start + PAGE_SIZE);

    pageSlice.forEach(({ epic, feat, story, epicIdx, featIdx, storyIdx }) => {
        const section = document.createElement('div');
        section.style.marginBottom = '1.25rem';

        const titleEl = document.createElement('h6');
        titleEl.style.cssText = 'font-weight:700;color:var(--warning-color);margin-bottom:.5rem;display:flex;align-items:center;gap:.5rem';
        titleEl.innerHTML = `<i class="fas fa-book"></i> <span style="flex:1">${esc(story.title)}</span>
            <span style="font-size:.7rem;font-weight:400;color:var(--text-secondary)">${epic.name} › ${feat.name}</span>`;
        section.appendChild(titleEl);

        // Tasks sub-section
        if (story.tasks?.length > 0) {
            const tHead = document.createElement('p');
            tHead.style.cssText = 'font-size:.8rem;font-weight:600;color:var(--text-secondary);margin:.5rem 0 .3rem';
            tHead.innerHTML = `<i class="fas fa-tasks" style="color:var(--success-color)"></i> Tasks`;
            section.appendChild(tHead);

            story.tasks.forEach((task, tIdx) => {
                section.appendChild(renderTaskRow(task, {
                    onEdit: t => openEditModal({ type: 'task', data: t, onSave: u => {
                        Object.assign(t, u); renderPage(listArea(), _currentPage);
                    }})
                }));
            });
        }

        // Test cases sub-section
        if (story.testCases?.length > 0) {
            const tcHead = document.createElement('p');
            tcHead.style.cssText = 'font-size:.8rem;font-weight:600;color:var(--text-secondary);margin:.75rem 0 .3rem';
            tcHead.innerHTML = `<i class="fas fa-vial" style="color:var(--text-secondary)"></i> Test Cases`;
            section.appendChild(tcHead);

            story.testCases.forEach((tc, tcIdx) => {
                section.appendChild(renderTestCaseRow(tc, {
                    onEdit: t => openEditModal({ type: 'testcase', data: t, onSave: u => {
                        Object.assign(t, u); renderPage(listArea(), _currentPage);
                    }})
                }));
            });
        }

        listEl.appendChild(section);
    });

    // Pagination
    const totalPages = Math.ceil(_allStories.length / PAGE_SIZE);
    if (totalPages > 1) {
        const pag = document.createElement('div');
        pag.style.cssText = 'display:flex;justify-content:center;gap:.5rem;margin-top:1.25rem';
        for (let p = 0; p < totalPages; p++) {
            const pb = document.createElement('button');
            pb.className = `btn btn-sm ${p === page ? 'btn-primary' : 'btn-outline-secondary'}`;
            pb.textContent = p + 1;
            pb.addEventListener('click', () => renderPage(listEl, p));
            pag.appendChild(pb);
        }
        listEl.appendChild(pag);
    }

    // Skip step option
    const hint = document.createElement('p');
    hint.className = 'text-muted small mt-3';
    hint.innerHTML = '<i class="fas fa-info-circle"></i> Uncheck items to exclude. <button class="btn-ow-skip" id="btn-skip-step5">Skip tasks &amp; tests</button> to add them later.';
    listEl.appendChild(hint);
    document.getElementById('btn-skip-step5')?.addEventListener('click', () => {
        WizardState.epics.forEach(e => (e.features ?? []).forEach(f => (f.userStories ?? []).forEach(s => { s.tasks = []; s.testCases = []; })));
        WizardState.emit('step:skip', 5);
    });
}

export async function saveStep5() {
    for (const entry of _allStories) {
        const { story } = entry;
        if (!story.id) continue;
        const tasks     = (story.tasks     ?? []).filter(t  => t.selected !== false);
        const testCases = (story.testCases ?? []).filter(tc => tc.selected !== false);

        const saved = await apiFetch('/api/onboard/save-tasks-tests', {
            method: 'POST',
            body: JSON.stringify({
                storyId: story.id,
                tasks: tasks.map(t => ({ id: t.id, title: t.title, description: t.description,
                    priority: t.priority, optimisticHours: t.optimisticHours,
                    mostLikelyHours: t.mostLikelyHours, pessimisticHours: t.pessimisticHours })),
                testCases: testCases.map(tc => ({ id: tc.id, title: tc.title,
                    steps: tc.steps, expectedResult: tc.expectedResult }))
            })
        });
        WizardState.setStoryTasksTests(story.id, saved.tasks ?? [], saved.testCases ?? []);
    }
    updateSummaryBar();
}

function esc(s) { return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
