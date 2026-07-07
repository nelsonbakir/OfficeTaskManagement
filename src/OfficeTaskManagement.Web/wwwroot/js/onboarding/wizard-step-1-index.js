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
    const wrap = LOADING_WRAP();
    const stats = STATS_DIV();
    if (!wrap) return;

    if (WizardState.indexStatus) {
        showIndexComplete(WizardState.indexStatus, false);
        wireReindexBtn({ hasRepoUrl }, stats, wrap);
        return;
    }

    setStatus('Checking indexing status…', 5);

    try {
        const data = await apiFetch(`/api/agent/index-status/${WizardState.projectId}`);
        WizardState.setIndexStatus(data);
        
        if (data.chunkCount > 0) {
            // Previously indexed scenario
            showIndexComplete(data, false); // Do not emit step1:complete to avoid auto-advance
            wireReindexBtn({ hasRepoUrl }, stats, wrap);
        } else {
            // Not indexed scenario
            document.getElementById('btn-wizard-next')?.setAttribute('disabled', 'true');
            wrap.innerHTML = `
                <div class="text-center py-4">
                    <div class="mb-3" style="font-size:2.5rem;color:var(--text-secondary)">
                        <i class="fas fa-database"></i>
                    </div>
                    <h5 style="font-weight:700">Codebase not indexed</h5>
                    <p class="text-muted small mx-auto" style="max-width:400px">
                        This codebase has not been indexed yet. Before the AI can suggest epics, features, stories, and tasks, you need to run the codebase indexing pipeline.
                    </p>
                    <button class="btn btn-primary btn-sm px-4 mt-2" id="btn-start-indexing">
                        <i class="fas fa-play me-1"></i> Start Codebase Indexing
                    </button>
                </div>`;
                
            document.getElementById('btn-start-indexing')?.addEventListener('click', async () => {
                // Restore loading spinner view
                wrap.innerHTML = `
                    <div class="onboard-spinner"></div>
                    <h5 id="indexing-status-text">Preparing indexing pipeline…</h5>
                    <div class="onboard-progress-bar">
                        <div class="onboard-progress-fill" id="indexing-progress-fill"></div>
                    </div>`;
                await runIndexingPipeline({ hasRepoUrl });
            });
        }
    } catch (err) {
        setStatus(`⚠ Failed to check status: ${err.message}`, 100, true);
        document.getElementById('btn-wizard-next')?.setAttribute('disabled', 'true');
    }
}

function wireReindexBtn({ hasRepoUrl }, stats, wrap) {
    let actionArea = document.getElementById('indexing-action-area');
    if (!actionArea) {
        actionArea = document.createElement('div');
        actionArea.id = 'indexing-action-area';
        actionArea.className = 'mt-3 text-center';
        stats.appendChild(actionArea);
    }
    
    actionArea.innerHTML = `
        <button class="btn btn-sm btn-outline-warning" id="btn-reindex-codebase">
            <i class="fas fa-sync-alt"></i> Re-index / Sync Codebase
        </button>`;
        
    document.getElementById('btn-reindex-codebase')?.addEventListener('click', async () => {
        if (!confirm("Are you sure you want to re-index the codebase? This might take a few minutes.")) return;
        actionArea.innerHTML = '';
        WizardState.setIndexStatus(null);
        // Restore loading spinner view
        wrap.innerHTML = `
            <div class="onboard-loading-wrapper" id="indexing-loading">
                <div class="onboard-spinner"></div>
                <h5 id="indexing-status-text">Preparing indexing pipeline…</h5>
                <div class="onboard-progress-bar">
                    <div class="onboard-progress-fill" id="indexing-progress-fill"></div>
                </div>
            </div>`;
        stats.style.display = 'none';
        document.getElementById('btn-wizard-next')?.setAttribute('disabled', 'true');
        await runIndexingPipeline({ hasRepoUrl });
    });
}

async function runIndexingPipeline({ hasRepoUrl }) {
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
            setStatus(`Indexed ${data.chunkCount ?? data.indexedChunks ?? '…'} code chunks…`, pct);

            if (data.indexingStatus === 'Completed' || data.chunkCount > 0) {
                clearInterval(_pollingTimer);
                WizardState.setIndexStatus(data);
                showIndexComplete(data, true);
            }
        } catch { /* keep polling */ }

        if (attempts > 60) {
            clearInterval(_pollingTimer);
            const fallbackData = { chunkCount: '?', repositoryPath: '(local)' };
            WizardState.setIndexStatus(fallbackData);
            showIndexComplete(fallbackData, true);
        }
    }, 3000);
}

function showIndexComplete(data, emitComplete = true) {
    setStatus('Codebase indexed ✓', 100);

    const wrap = LOADING_WRAP();
    if (wrap) wrap.innerHTML = `
        <div class="text-center py-3">
            <div class="ow-index-complete-check mb-2"><i class="fas fa-check-circle" style="color:var(--success-color);font-size:2.5rem"></i></div>
            <h5 style="color:var(--success-color);font-weight:700">Indexing Pipeline Complete!</h5>
            <p class="text-muted small">The codebase is ready for AI-powered discovery.</p>
        </div>`;

    const stats = STATS_DIV();
    if (stats) {
        stats.style.display = '';
        const path  = stats.querySelector('#stat-repo-path');
        const count = stats.querySelector('#stat-chunks-count');
        if (path)  path.textContent  = data.repositoryPath ?? 'local';
        if (count) count.textContent = data.chunkCount ?? data.indexedChunks ?? '–';
    }

    document.getElementById('btn-wizard-next')?.removeAttribute('disabled');

    if (emitComplete) {
        WizardState.emit('step1:complete', data);
    }
}
