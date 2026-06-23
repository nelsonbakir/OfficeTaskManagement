/**
 * onboard-wizard.js — Thin orchestrator
 * Wires step modules together, handles state restoration, navigation,
 * checkpoint saves, and summary bar updates.
 *
 * All heavy logic lives in:  wwwroot/js/onboarding/wizard-step-*.js
 * All rendering utilities:   wwwroot/js/onboarding/wizard-ui.js
 * Shared state:              wwwroot/js/onboarding/wizard-state.js
 */
import { WizardState, apiFetch }       from './onboarding/wizard-state.js';
import { updateStepNav, flashCheckpoint, updateSummaryBar } from './onboarding/wizard-ui.js';
import { runStep1 }                     from './onboarding/wizard-step-1-index.js';
import { runStep2, saveStep2 }          from './onboarding/wizard-step-2-epics.js';
import { runStep3, saveStep3 }          from './onboarding/wizard-step-3-features.js';
import { runStep4, saveStep4 }          from './onboarding/wizard-step-4-stories.js';
import { runStep5, saveStep5 }          from './onboarding/wizard-step-5-tasks.js';
import { runStep6 }                     from './onboarding/wizard-step-6-review.js';

(function () {
    'use strict';

    const container  = document.getElementById('wizard-panels-container');
    if (!container) return;

    const projectId  = parseInt(container.dataset.projectId, 10);
    const hasRepoUrl = container.dataset.hasRepoUrl === 'true';

    WizardState.init(projectId);

    const TOTAL_STEPS = 6;
    const btnNext = document.getElementById('btn-wizard-next');
    const btnPrev = document.getElementById('btn-wizard-prev');

    // ── Step runner map ───────────────────────────────────────────────────────
    const stepRunners = {
        1: () => runStep1({ hasRepoUrl }),
        2: () => runStep2(),
        3: () => runStep3(),
        4: () => runStep4(),
        5: () => runStep5(),
        6: () => runStep6()
    };

    // ── Step savers (called BEFORE advancing) ─────────────────────────────────
    const stepSavers = {
        2: saveStep2,
        3: saveStep3,
        4: saveStep4,
        5: saveStep5,
    };

    // ── Panel display ─────────────────────────────────────────────────────────
    function showPanel(step) {
        document.querySelectorAll('.wizard-panel').forEach(p => p.classList.remove('active'));
        const panel = document.querySelector(`.wizard-panel[data-step="${step}"]`);
        if (panel) panel.classList.add('active');
        updateStepNav(step, TOTAL_STEPS);
        updateSummaryBar();
    }

    async function transitionToStep(step) {
        showPanel(step);
        if (stepRunners[step]) await stepRunners[step]();
    }

    // ── Initial load ──────────────────────────────────────────────────────────
    async function loadInitialState() {
        setNavBusy(true);
        try {
            const data = await apiFetch(`/api/onboard/state/${projectId}`);

            WizardState.setAnalysisResult({
                techStack:              data.techStack,
                projectSummary:         data.projectSummary,
                testOverview:           data.testOverview,
                testsAbsentOrIncomplete: data.testsAbsentOrIncomplete
            });

            if (data.epics?.length > 0) {
                WizardState.setEpics(data.epics.map(e => ({
                    ...e,
                    features: (e.features ?? []).map(f => ({
                        ...f,
                        userStories: (f.userStories ?? []).map(s => ({
                            ...s, selected: true,
                            tasks:     (s.tasks     ?? []).map(t  => ({ ...t,  selected: true })),
                            testCases: (s.testCases ?? []).map(tc => ({ ...tc, selected: true }))
                        }))
                    }))
                })));

                // Resume at the checkpoint step
                const resumeStep = Math.max(1, Math.min(TOTAL_STEPS, data.lastCompletedStep ?? 0));
                WizardState.setStep(resumeStep);
                await transitionToStep(resumeStep);
            } else {
                WizardState.setStep(1);
                await transitionToStep(1);
            }
        } catch (err) {
            console.error('Failed to load onboarding state', err);
            WizardState.setStep(1);
            await transitionToStep(1);
        } finally {
            setNavBusy(false);
        }
    }

    // ── Next button ───────────────────────────────────────────────────────────
    btnNext?.addEventListener('click', async () => {
        const step = WizardState.currentStep;

        if (step === TOTAL_STEPS) {
            // Completion flow
            await completeOnboarding();
            return;
        }

        setNavBusy(true);
        try {
            // Save current step data if it has a saver
            if (stepSavers[step]) await stepSavers[step]();

            // Persist checkpoint
            await WizardState.saveCheckpoint(step);
            flashCheckpoint();

            WizardState.advanceStep();
            await transitionToStep(WizardState.currentStep);
        } catch (err) {
            showToast(`Could not save: ${err.message}`, 'danger');
        } finally {
            setNavBusy(false);
        }
    });

    // ── Prev button ───────────────────────────────────────────────────────────
    btnPrev?.addEventListener('click', async () => {
        if (WizardState.currentStep <= 1) return;
        setNavBusy(true);
        WizardState.goBack();
        await transitionToStep(WizardState.currentStep);
        setNavBusy(false);
    });

    // ── Skip step event ───────────────────────────────────────────────────────
    WizardState.on('step:skip', async (skippedStep) => {
        await WizardState.saveCheckpoint(skippedStep);
        flashCheckpoint();
        WizardState.advanceStep();
        await transitionToStep(WizardState.currentStep);
    });

    // ── Step 1 complete → auto-advance to Step 2 ──────────────────────────────
    WizardState.on('step1:complete', async () => {
        await WizardState.saveCheckpoint(1);
        flashCheckpoint();
        WizardState.setStep(2);
        await transitionToStep(2);
    });

    // ── Final completion ──────────────────────────────────────────────────────
    async function completeOnboarding() {
        setNavBusy(true);
        try {
            await apiFetch(`/api/onboard/complete/${projectId}`, { method: 'POST' });
            // Redirect to project details
            window.location.href = `/Projects/Details/${projectId}`;
        } catch (err) {
            showToast(`Could not complete onboarding: ${err.message}`, 'danger');
            setNavBusy(false);
        }
    }

    // ── Utilities ─────────────────────────────────────────────────────────────
    function setNavBusy(busy) {
        if (btnNext) btnNext.disabled = busy;
        if (btnPrev) btnPrev.disabled = busy;
        const spinner = document.getElementById('btn-next-spinner');
        if (spinner) spinner.style.display = busy ? 'inline-block' : 'none';
    }

    function showToast(message, type = 'danger') {
        const existing = document.getElementById('ow-toast');
        if (existing) existing.remove();
        const toast = document.createElement('div');
        toast.id = 'ow-toast';
        toast.style.cssText = `position:fixed;bottom:1.5rem;right:1.5rem;z-index:9999;
            background:${type === 'danger' ? 'var(--danger-color)' : 'var(--success-color)'};
            color:#fff;padding:.75rem 1.25rem;border-radius:var(--ow-radius);
            font-size:.875rem;box-shadow:0 4px 20px rgba(0,0,0,.2);
            animation:ow-slide-up .3s ease both`;
        toast.textContent = message;
        document.body.appendChild(toast);
        setTimeout(() => toast.remove(), 5000);
    }

    // ── Boot ──────────────────────────────────────────────────────────────────
    loadInitialState();

})();
