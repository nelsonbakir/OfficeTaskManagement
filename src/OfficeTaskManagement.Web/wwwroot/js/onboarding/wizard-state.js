/**
 * wizard-state.js
 * Shared reactive state store for the onboarding wizard.
 * Uses a simple event-emitter pattern — no external dependencies.
 */

export const WizardState = (() => {
    // ── Core data ────────────────────────────────────────────────────────────
    let _projectId    = 0;
    let _currentStep  = 1;
    let _totalSteps   = 6;

    // The main tree that grows as the user confirms each level
    let _epics        = [];   // [{id, name, description, selected, features:[…]}]
    let _indexStatus  = null;
    let _isAnalyzing  = false;
    const _activeControllers = new Set();

    // Codebase analysis summary (set after Step 2 AI call)
    let _analysisResult = {
        techStack:              'N/A',
        projectSummary:         '',
        testOverview:           'N/A',
        testsAbsentOrIncomplete: true
    };

    // ── Event bus ────────────────────────────────────────────────────────────
    const _listeners = {};

    function on(event, cb) {
        (_listeners[event] ??= []).push(cb);
    }

    function off(event, cb) {
        if (!_listeners[event]) return;
        _listeners[event] = _listeners[event].filter(l => l !== cb);
    }

    function emit(event, data) {
        (_listeners[event] ?? []).forEach(cb => cb(data));
    }

    // ── Checkpoint persistence ───────────────────────────────────────────────
    async function saveCheckpoint(step) {
        try {
            await fetch(`/api/onboard/checkpoint/${_projectId}/${step}`, {
                method: 'PATCH',
                headers: { 'RequestVerificationToken': getAntiForgeryToken() }
            });
            emit('checkpoint:saved', { step });
        } catch { /* non-critical */ }
    }

    // ── Step helpers ─────────────────────────────────────────────────────────
    function setStep(step) {
        _currentStep = Math.max(1, Math.min(_totalSteps, step));
        emit('step:changed', _currentStep);
    }

    function advanceStep() { setStep(_currentStep + 1); }
    function goBack()       { setStep(_currentStep - 1); }

    // ── Tree helpers ─────────────────────────────────────────────────────────
    function setEpics(epics)             { _epics = epics; emit('epics:updated', _epics); }
    function setEpicFeatures(epicId, features) {
        const epic = _epics.find(e => e.id === epicId);
        if (epic) { epic.features = features; emit('features:updated', { epicId, features }); }
    }
    function setFeatureStories(featureId, stories) {
        for (const epic of _epics) {
            const feat = (epic.features ?? []).find(f => f.id === featureId);
            if (feat) { feat.userStories = stories; emit('stories:updated', { featureId, stories }); break; }
        }
    }
    function setStoryTasksTests(storyId, tasks, testCases) {
        for (const epic of _epics) {
            for (const feat of (epic.features ?? [])) {
                const story = (feat.userStories ?? []).find(s => s.id === storyId);
                if (story) {
                    story.tasks     = tasks;
                    story.testCases = testCases;
                    emit('tasks:updated', { storyId, tasks, testCases });
                    return;
                }
            }
        }
    }

    function setAnalysisResult(result) {
        _analysisResult = result;
        emit('analysis:updated', _analysisResult);
    }

    // ── Summary counters ─────────────────────────────────────────────────────
    function getSummary() {
        let epics = 0, features = 0, stories = 0, tasks = 0, tests = 0;
        for (const e of _epics) {
            if (!e.selected) continue; epics++;
            for (const f of (e.features ?? [])) {
                if (!f.selected) continue; features++;
                for (const s of (f.userStories ?? [])) {
                    if (!s.selected) continue; stories++;
                    tasks += (s.tasks     ?? []).filter(t  => t.selected !== false).length;
                    tests += (s.testCases ?? []).filter(tc => tc.selected !== false).length;
                }
            }
        }
        return { epics, features, stories, tasks, tests };
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return {
        // setup
        init(projectId) { _projectId = projectId; },

        // events
        on, off, emit,

        // step navigation
        get currentStep() { return _currentStep; },
        get totalSteps()  { return _totalSteps; },
        setStep, advanceStep, goBack,
        saveCheckpoint,

        // data
        get epics()          { return _epics; },
        get analysisResult() { return _analysisResult; },
        get projectId()      { return _projectId; },
        get indexStatus()    { return _indexStatus; },
        get isAnalyzing()    { return _isAnalyzing; },
        setIndexStatus(status) { _indexStatus = status; },
        setAnalyzing(val)    { _isAnalyzing = val; },
        registerController(c) { _activeControllers.add(c); },
        unregisterController(c) { _activeControllers.delete(c); },
        abortAll() { _activeControllers.forEach(c => c.abort()); _activeControllers.clear(); },
        setEpics, setEpicFeatures, setFeatureStories, setStoryTasksTests,
        setAnalysisResult, getSummary,
    };
})();

// ── Shared CSRF token reader ──────────────────────────────────────────────
export function getAntiForgeryToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
}

// ── Shared fetch helper (throws on non-OK) ────────────────────────────────
export async function apiFetch(url, options = {}) {
    const timeout = options.timeout ?? 300000; // 5 minutes default
    const controller = new AbortController();
    const id = setTimeout(() => controller.abort(), timeout);

    WizardState.registerController(controller);

    try {
        const res = await fetch(url, {
            ...options,
            signal: controller.signal,
            keepalive: true, // Allow request to outlive the page
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken(),
                ...(options.headers ?? {})
            }
        });
        clearTimeout(id);
        WizardState.unregisterController(controller);
        if (!res.ok) {
            const text = await res.text().catch(() => res.statusText);
            throw new Error(text || `HTTP ${res.status}`);
        }
        return res.json();
    } catch (err) {
        clearTimeout(id);
        WizardState.unregisterController(controller);
        if (err.name === 'AbortError') {
            throw new Error('Request timed out after 5 minutes.');
        }
        throw err;
    }
}
