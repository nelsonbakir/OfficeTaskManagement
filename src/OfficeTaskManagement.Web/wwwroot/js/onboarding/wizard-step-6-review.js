/**
 * wizard-step-6-review.js — Final review tree (Step 6)
 * Renders a collapsible, expandable summary of everything that was built.
 */
import { WizardState } from './wizard-state.js';
import { calcPert } from './wizard-ui.js';

const area = () => document.getElementById('review-tree-area');
const countersEl = () => document.getElementById('review-counters');

export function runStep6() {
    const l = area();
    if (!l) return;
    l.innerHTML = '';

    const summary = WizardState.getSummary();
    renderCounters(summary);

    WizardState.epics.forEach((epic, ei) => {
        if (!epic.selected) return;

        const epicWrap = document.createElement('div');
        epicWrap.className = 'ow-tree-epic';

        const epicHeader = document.createElement('div');
        epicHeader.className = 'ow-accordion-header';
        epicHeader.innerHTML = `
            <span class="ow-tree-dot"></span>
            <span style="font-weight:700;color:var(--text-primary);flex:1">${esc(epic.name)}</span>
            <span style="font-size:.75rem;color:var(--text-secondary)">${(epic.features ?? []).filter(f=>f.selected).length} features</span>
            <i class="fas fa-chevron-down ow-accordion-arrow"></i>`;

        const epicBody = document.createElement('div');
        epicBody.className = 'ow-accordion-body';

        epicHeader.addEventListener('click', () => {
            epicHeader.classList.toggle('collapsed');
            epicBody.style.display = epicHeader.classList.contains('collapsed') ? 'none' : '';
        });

        (epic.features ?? []).forEach((feat, fi) => {
            if (!feat.selected) return;

            const featWrap = document.createElement('div');
            featWrap.className = 'ow-tree-feat';

            const featHeader = document.createElement('div');
            featHeader.className = 'ow-accordion-header';
            featHeader.style.background = 'rgba(98,100,167,.05)';
            featHeader.style.borderColor = 'rgba(98,100,167,.15)';
            featHeader.innerHTML = `
                <span class="ow-tree-dot" style="background:#6264A7"></span>
                <span style="flex:1;font-weight:600;font-size:.875rem">${esc(feat.name)}</span>
                <span style="font-size:.72rem;color:var(--text-secondary)">${(feat.userStories ?? []).filter(s=>s.selected).length} stories</span>
                <i class="fas fa-chevron-down ow-accordion-arrow"></i>`;

            const featBody = document.createElement('div');
            featBody.className = 'ow-accordion-body';

            featHeader.addEventListener('click', () => {
                featHeader.classList.toggle('collapsed');
                featBody.style.display = featHeader.classList.contains('collapsed') ? 'none' : '';
            });

            (feat.userStories ?? []).forEach((story, si) => {
                if (!story.selected) return;

                const storyWrap = document.createElement('div');
                storyWrap.className = 'ow-tree-story';

                const taskCount = (story.tasks ?? []).filter(t => t.selected !== false).length;
                const testCount = (story.testCases ?? []).filter(tc => tc.selected !== false).length;

                const storyHeader = document.createElement('div');
                storyHeader.className = 'ow-accordion-header';
                storyHeader.style.background = 'rgba(255,185,0,.04)';
                storyHeader.style.borderColor = 'rgba(255,185,0,.2)';
                storyHeader.innerHTML = `
                    <span class="ow-tree-dot" style="background:var(--warning-color)"></span>
                    <span style="flex:1;font-size:.85rem;font-weight:600">${esc(story.title)}</span>
                    <span style="font-size:.7rem;color:var(--text-secondary)">${taskCount}T / ${testCount}TC</span>
                    <i class="fas fa-chevron-down ow-accordion-arrow"></i>`;

                const storyBody = document.createElement('div');
                storyBody.className = 'ow-accordion-body';

                storyHeader.addEventListener('click', () => {
                    storyHeader.classList.toggle('collapsed');
                    storyBody.style.display = storyHeader.classList.contains('collapsed') ? 'none' : '';
                });

                // Tasks
                if (taskCount > 0) {
                    (story.tasks ?? []).filter(t => t.selected !== false).forEach(task => {
                        const pert = calcPert(task.optimisticHours, task.mostLikelyHours, task.pessimisticHours);
                        const row = document.createElement('div');
                        row.className = 'ow-tree-task';
                        row.innerHTML = `
                            <span class="ow-tree-dot"></span>
                            <span style="font-size:.82rem">${esc(task.title)}</span>
                            <span style="font-size:.7rem;color:var(--text-secondary);margin-left:.5rem">PERT: ${pert}h</span>`;
                        storyBody.appendChild(row);
                    });
                }

                // Test cases
                if (testCount > 0) {
                    (story.testCases ?? []).filter(tc => tc.selected !== false).forEach(tc => {
                        const row = document.createElement('div');
                        row.className = 'ow-tree-task';
                        row.innerHTML = `<i class="fas fa-vial" style="font-size:.65rem;color:var(--text-secondary);margin-right:.3rem"></i> ${esc(tc.title)}`;
                        storyBody.appendChild(row);
                    });
                }

                storyWrap.appendChild(storyHeader);
                storyWrap.appendChild(storyBody);
                featBody.appendChild(storyWrap);
            });

            featWrap.appendChild(featHeader);
            featWrap.appendChild(featBody);
            epicBody.appendChild(featWrap);
        });

        epicWrap.appendChild(epicHeader);
        epicWrap.appendChild(epicBody);
        l.appendChild(epicWrap);
    });
}

function renderCounters(summary) {
    const el = countersEl();
    if (!el) return;
    el.innerHTML = `
        <div class="d-flex gap-3 flex-wrap">
            ${stat('layer-group', summary.epics,    'Epics',    'var(--primary-color)')}
            ${stat('puzzle-piece', summary.features, 'Features', '#6264A7')}
            ${stat('book',         summary.stories,  'Stories',  'var(--warning-color)')}
            ${stat('tasks',        summary.tasks,    'Tasks',    'var(--success-color)')}
            ${stat('vial',         summary.tests,    'Tests',    'var(--text-secondary)')}
        </div>`;
}

function stat(icon, val, label, color) {
    return `
        <div class="glass-card" style="padding:.75rem 1.25rem;text-align:center;min-width:80px;margin-bottom:0">
            <div style="font-size:1.4rem;font-weight:800;color:${color}">${val}</div>
            <div style="font-size:.72rem;color:var(--text-secondary);font-weight:600">
                <i class="fas fa-${icon}" style="margin-right:.25rem"></i>${label}
            </div>
        </div>`;
}

function esc(s) { return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
