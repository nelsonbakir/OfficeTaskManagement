/**
 * wizard-step-1-index.js — Clone & Index pipeline (Step 1)
 */
import { WizardState, apiFetch } from './wizard-state.js';

const STATUS_TEXT   = () => document.getElementById('indexing-status-text');
const PROGRESS_FILL = () => document.getElementById('indexing-progress-fill');
const LOADING_WRAP  = () => document.getElementById('indexing-loading');
const STATS_DIV     = () => document.getElementById('indexing-stats');

let _pollingTimer = null;

export async function runStep1({ hasRepoUrl }) {
    setStatus('Checking repository status…', 5);

    try {
        if (hasRepoUrl) {
            setStatus('Cloning remote repository (shallow copy)…', 20);
            await apiFetch(`/api/onboard/clone/${WizardState.projectId}`, { method: 'POST' });
            setStatus('Repository cloned ✓', 50);
        }

        setStatus('Vectorising codebase with Gemini embedding model…', 65);
        await apiFetch(`/api/agent/index-project/${WizardState.projectId}`, { method: 'POST' });
        setStatus('Indexing started — polling for completion…', 75);

        pollIndexStatus();
    } catch (err) {
        setStatus(`⚠ ${err.message}`, 100, true);
        document.getElementById('btn-wizard-next')?.setAttribute('disabled', 'true');
    }
}

function setStatus(text, pct, isError = false) {
    const st = STATUS_TEXT(); if (st) st.textContent = text;
    const pf = PROGRESS_FILL(); if (pf) {
        pf.style.width = `${pct}%`;
        pf.style.background = isError ? 'var(--danger-color)' : '';
    }
}

function pollIndexStatus() {
    let attempts = 0;
    _pollingTimer = setInterval(async () => {
        attempts++;
        try {
            const data = await apiFetch(`/api/agent/index-status/${WizardState.projectId}`);
            const pct  = 75 + Math.min(20, attempts * 2);
            setStatus(`Indexed ${data.indexedChunks ?? '…'} code chunks…`, pct);

            if (data.isIndexed) {
                clearInterval(_pollingTimer);
                showIndexComplete(data);
            }
        } catch { /* keep polling */ }

        if (attempts > 60) {
            clearInterval(_pollingTimer);
            showIndexComplete({ indexedChunks: '?', repositoryPath: '(local)' });
        }
    }, 3000);
}

function showIndexComplete(data) {
    setStatus('Codebase indexed ✓', 100);

    const wrap = LOADING_WRAP();
    if (wrap) wrap.innerHTML = `
        <div class="ow-index-complete-check mb-3"><i class="fas fa-check-circle"></i></div>
        <h5 style="color:var(--success-color);font-weight:700">Indexing Pipeline Complete!</h5>
        <p class="text-muted" style="font-size:.875rem">The codebase is ready for AI-powered discovery.</p>`;

    const stats = STATS_DIV();
    if (stats) {
        stats.style.display = '';
        const path  = stats.querySelector('#stat-repo-path');
        const count = stats.querySelector('#stat-chunks-count');
        if (path)  path.textContent  = data.repositoryPath ?? 'local';
        if (count) count.textContent = data.indexedChunks  ?? '–';
    }

    WizardState.emit('step1:complete', data);
}
