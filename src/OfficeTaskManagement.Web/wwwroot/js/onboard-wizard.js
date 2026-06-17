/**
 * Codebase Onboarding Wizard JS Controller
 * OfficeTaskManagement PMP Tool
 */
(function () {
    'use strict';

    const container = document.getElementById('wizard-panels-container');
    if (!container) return;

    const projectId = parseInt(container.dataset.projectId);
    const hasRepoUrl = container.dataset.hasRepoUrl === 'true';
    const repoUrl = container.dataset.repoUrl || '';

    // Wizard State Tree
    let wizardState = {
        projectId: projectId,
        techStack: 'N/A',
        projectSummary: 'N/A',
        testOverview: 'N/A',
        testsAbsentOrIncomplete: true,
        epics: [] // array of { name, description, selected: true, features: [] }
    };

    let currentStep = 1;
    const maxStep = 6;

    // UI Elements
    const btnNext = document.getElementById('btn-wizard-next');
    const btnPrev = document.getElementById('btn-wizard-prev');
    const progressBar = document.getElementById('step-progress-bar');
    const editModalEl = document.getElementById('wizardEditModal');
    const editModal = editModalEl ? new bootstrap.Modal(editModalEl) : null;

    // Initialize Onboarding Wizard
    document.addEventListener('DOMContentLoaded', () => {
        loadInitialState();
    });

    async function loadInitialState() {
        try {
            const res = await fetch(`/api/onboard/state/${projectId}`);
            if (res.ok) {
                const data = await res.json();
                
                wizardState.techStack = data.techStack || 'N/A';
                wizardState.projectSummary = data.projectSummary || '';
                wizardState.testOverview = data.testOverview || 'N/A';
                wizardState.testsAbsentOrIncomplete = data.testsAbsentOrIncomplete;

                if (data.epics && data.epics.length > 0) {
                    wizardState.epics = data.epics.map(epic => ({
                        id: epic.id,
                        name: epic.name,
                        description: epic.description,
                        selected: epic.selected !== false,
                        features: (epic.features || []).map(feat => ({
                            id: feat.id,
                            name: feat.name,
                            description: feat.description,
                            selected: feat.selected !== false,
                            userStories: (feat.userStories || []).map(story => ({
                                id: story.id,
                                title: story.title,
                                description: story.description,
                                acceptanceCriteria: story.acceptanceCriteria,
                                priority: story.priority,
                                selected: story.selected !== false,
                                tasks: (story.tasks || []).map(t => ({
                                    id: t.id,
                                    title: t.title,
                                    description: t.description,
                                    priority: t.priority,
                                    optimisticHours: t.optimisticHours,
                                    mostLikelyHours: t.mostLikelyHours,
                                    pessimisticHours: t.pessimisticHours,
                                    selected: true
                                })),
                                testCases: (story.testCases || []).map(tc => ({
                                    id: tc.id,
                                    title: tc.title,
                                    steps: tc.steps,
                                    expectedResult: tc.expectedResult,
                                    selected: true
                                }))
                            }))
                        }))
                    }));

                    let hasFeatures = false;
                    let hasStories = false;
                    let hasTasks = false;

                    for (const epic of wizardState.epics) {
                        if (epic.features && epic.features.length > 0) {
                            hasFeatures = true;
                            for (const feat of epic.features) {
                                if (feat.userStories && feat.userStories.length > 0) {
                                    hasStories = true;
                                    for (const story of feat.userStories) {
                                        if ((story.tasks && story.tasks.length > 0) || (story.testCases && story.testCases.length > 0)) {
                                            hasTasks = true;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (hasTasks) {
                        currentStep = 5;
                    } else if (hasStories) {
                        currentStep = 4;
                    } else if (hasFeatures) {
                        currentStep = 3;
                    } else {
                        currentStep = 2;
                    }

                    if (wizardState.projectSummary) {
                        const ovCard = document.getElementById('overview-card');
                        if (ovCard) ovCard.style.display = 'block';
                        const badgeTech = document.getElementById('onboard-badge-tech');
                        if (badgeTech) badgeTech.textContent = wizardState.techStack;
                        const ovSumm = document.getElementById('onboard-overview-summary');
                        if (ovSumm) ovSumm.textContent = wizardState.projectSummary;
                        
                        const badgeCoverage = document.getElementById('onboard-badge-coverage');
                        if (badgeCoverage) {
                            badgeCoverage.textContent = wizardState.testsAbsentOrIncomplete ? "⚠️ Gaps Detected" : "✓ Comprehensive";
                            badgeCoverage.className = `badge badge-coverage ${wizardState.testsAbsentOrIncomplete ? "" : "passed"}`;
                        }
                        
                        const testsDesc = document.getElementById('onboard-overview-tests');
                        if (testsDesc) {
                            testsDesc.textContent = wizardState.testOverview;
                            testsDesc.className = `test-overview-desc ${wizardState.testsAbsentOrIncomplete ? "warning" : "passed"}`;
                        }
                    }

                    await transitionToStep(currentStep);
                    updateStepNav();
                } else {
                    currentStep = 1;
                    updateStepNav();
                    startCloneAndIndexPipeline();
                }
            } else {
                currentStep = 1;
                updateStepNav();
                startCloneAndIndexPipeline();
            }
        } catch (err) {
            console.error("Failed to load initial state", err);
            currentStep = 1;
            updateStepNav();
            startCloneAndIndexPipeline();
        }
    }

    // ── STEP 1: Git Clone & Index Pipeline ───────────────────────────────────
    async function startCloneAndIndexPipeline() {
        const statusText = document.getElementById('indexing-status-text');
        const progressFill = document.getElementById('indexing-progress-fill');

        try {
            if (hasRepoUrl) {
                statusText.textContent = 'Cloning remote repository (shallow copy)...';
                progressFill.style.width = '20%';

                const cloneRes = await fetch(`/api/onboard/clone/${projectId}`, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': getAntiForgeryToken() }
                });

                if (!cloneRes.ok) {
                    const errText = await cloneRes.text();
                    throw new Error(`Cloning failed: ${errText}`);
                }
                
                progressFill.style.width = '50%';
            }

            statusText.textContent = 'Vectorizing codebase and building index...';
            progressFill.style.width = '70%';

            // Trigger Indexing in background (non-blocking)
            await fetch(`/api/agent/index-project/${projectId}`, {
                method: 'POST',
                headers: { 'RequestVerificationToken': getAntiForgeryToken() }
            });

            // Start Polling Index Status
            pollIndexStatus();
        } catch (err) {
            statusText.textContent = `Error: ${err.message}`;
            progressFill.style.background = '#dc3545';
            btnNext.disabled = true;
        }
    }

    function pollIndexStatus() {
        const statusText = document.getElementById('indexing-status-text');
        const progressFill = document.getElementById('indexing-progress-fill');
        const loadingWrapper = document.getElementById('indexing-loading');
        const statsWrapper = document.getElementById('indexing-stats');

        const interval = setInterval(async () => {
            try {
                const res = await fetch(`/api/agent/index-status/${projectId}`);
                if (!res.ok) return;

                const status = await res.json();
                
                if (status.chunkCount > 0 && !status.needsSync) {
                    clearInterval(interval);
                    progressFill.style.width = '100%';
                    
                    document.getElementById('stat-repo-path').textContent = status.repositoryPath || 'N/A';
                    document.getElementById('stat-chunks-count').textContent = status.chunkCount;
                    
                    loadingWrapper.style.display = 'none';
                    statsWrapper.style.display = 'block';
                    btnNext.disabled = false;
                } else {
                    // Simulating index progress chunks count
                    statusText.textContent = `Indexing in progress (${status.chunkCount} code chunks vectorized)...`;
                    progressFill.style.width = '85%';
                }
            } catch (err) {
                console.error("Status polling failed", err);
            }
        }, 1500);
    }

    // ── STEP 2: Epics Discovery ───────────────────────────────────────────────
    async function loadEpicsStep() {
        if (wizardState.epics.length > 0) {
            renderEpicsList();
            return;
        }

        btnNext.disabled = true;
        const panel = document.getElementById('panel-2');
        const loader = showInlineLoader(panel, "Discovering system architecture & Epics...");

        try {
            const res = await fetch(`/api/onboard/analyze-project/${projectId}`, {
                method: 'POST',
                headers: { 'RequestVerificationToken': getAntiForgeryToken() }
            });

            if (!res.ok) throw new Error("Failed to analyze project codebase.");
            const data = await res.json();

            wizardState.techStack = data.techStack;
            wizardState.projectSummary = data.projectSummary;
            wizardState.testOverview = data.testOverview;
            wizardState.testsAbsentOrIncomplete = data.testsAbsentOrIncomplete;

            // Load suggested epics
            wizardState.epics = data.suggestedEpics.map(e => ({
                name: e.name,
                description: e.description,
                selected: true,
                features: []
            }));

            // Render Overview Badges
            document.getElementById('overview-card').style.display = 'block';
            document.getElementById('onboard-badge-tech').textContent = data.techStack;
            document.getElementById('onboard-overview-summary').textContent = data.projectSummary;
            
            const badgeCoverage = document.getElementById('onboard-badge-coverage');
            badgeCoverage.textContent = data.testsAbsentOrIncomplete ? "⚠️ Gaps Detected" : "✓ Comprehensive";
            badgeCoverage.className = `badge badge-coverage ${data.testsAbsentOrIncomplete ? "" : "passed"}`;
            
            const testsDesc = document.getElementById('onboard-overview-tests');
            testsDesc.textContent = data.testOverview;
            testsDesc.className = `test-overview-desc ${data.testsAbsentOrIncomplete ? "warning" : "passed"}`;

            loader.remove();
            renderEpicsList();
            btnNext.disabled = false;
        } catch (err) {
            loader.innerHTML = `<div class="text-danger"><i class="fas fa-exclamation-triangle"></i> ${err.message}</div>`;
        }
    }

    function renderEpicsList() {
        const container = document.getElementById('epics-list-container');
        container.innerHTML = '';

        wizardState.epics.forEach((epic, idx) => {
            const item = document.createElement('div');
            item.className = 'onboard-list-item';
            item.innerHTML = `
                <input type="checkbox" class="item-checkbox epic-check" data-idx="${idx}" ${epic.selected ? 'checked' : ''} />
                <div class="item-content">
                    <div class="item-title-row">
                        <h5 class="item-title">${escapeHtml(epic.name)}</h5>
                        <div class="item-actions">
                            <button class="btn-icon-action btn-edit" data-type="epic" data-epic="${idx}"><i class="fas fa-edit"></i></button>
                            <button class="btn-icon-action btn-icon-delete btn-delete" data-type="epic" data-epic="${idx}"><i class="fas fa-trash"></i></button>
                        </div>
                    </div>
                    <p class="item-desc">${escapeHtml(epic.description)}</p>
                </div>
            `;
            container.appendChild(item);
        });

        // Attach listeners
        container.querySelectorAll('.epic-check').forEach(cb => {
            cb.addEventListener('change', (e) => {
                wizardState.epics[parseInt(e.target.dataset.idx)].selected = e.target.checked;
            });
        });

        container.querySelectorAll('.btn-edit').forEach(btn => {
            btn.addEventListener('click', (e) => openEditModal(e.currentTarget));
        });

        container.querySelectorAll('.btn-delete').forEach(btn => {
            btn.addEventListener('click', (e) => deleteItem(e.currentTarget));
        });
    }

    // ── STEP 3: Features Discovery ─────────────────────────────────────────────
    async function loadFeaturesStep() {
        const container = document.getElementById('features-epics-accordion');
        container.innerHTML = '';

        const selectedEpics = wizardState.epics.filter(e => e.selected);
        if (selectedEpics.length === 0) {
            container.innerHTML = '<div class="alert alert-warning">No Epics selected. Please go back and select at least one Epic.</div>';
            return;
        }

        btnNext.disabled = true;

        for (let i = 0; i < wizardState.epics.length; i++) {
            const epic = wizardState.epics[i];
            if (!epic.selected) continue;

            // Render Epic Header Accordion Container
            const card = document.createElement('div');
            card.className = 'glass-card mb-3';
            card.innerHTML = `
                <div class="accordion-toggle d-flex justify-content-between align-items-center" data-bs-toggle="collapse" data-bs-target="#collapse-epic-${i}">
                    <h4 class="mb-0"><i class="fas fa-folder text-primary"></i> ${escapeHtml(epic.name)}</h4>
                    <span class="accordion-arrow"><i class="fas fa-chevron-down"></i></span>
                </div>
                <div id="collapse-epic-${i}" class="collapse show mt-3">
                    <div class="features-list" id="epic-features-${i}">
                        <div class="onboard-loading-wrapper py-3">
                            <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
                            <span class="ms-2 small">Scanning code paths...</span>
                        </div>
                    </div>
                    <button class="btn btn-sm btn-outline-secondary floating-add-btn btn-add-feature" data-epic="${i}">
                        <i class="fas fa-plus"></i> Add Feature to ${escapeHtml(epic.name)}
                    </button>
                </div>
            `;
            container.appendChild(card);

            // Fetch features from AI if empty
            if (epic.features.length === 0) {
                try {
                    const res = await fetch(`/api/onboard/analyze-features/${epic.id}`, {
                        method: 'POST',
                        headers: {
                            'RequestVerificationToken': getAntiForgeryToken()
                        }
                    });

                    if (res.ok) {
                        const data = await res.json();
                        epic.features = data.map(f => ({
                            id: f.id,
                            name: f.name,
                            description: f.description,
                            selected: true,
                            userStories: []
                        }));
                    }
                } catch (err) {
                    console.error("Failed to load features for epic", epic.name, err);
                }
            }

            renderFeaturesList(i);
        }

        btnNext.disabled = false;

        // Add features click handler
        container.querySelectorAll('.btn-add-feature').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const epicIdx = parseInt(e.currentTarget.dataset.epic);
                wizardState.epics[epicIdx].features.push({
                    name: 'New Feature',
                    description: 'Description of the feature',
                    selected: true,
                    userStories: []
                });
                renderFeaturesList(epicIdx);
            });
        });
    }

    function renderFeaturesList(epicIdx) {
        const epic = wizardState.epics[epicIdx];
        const listDiv = document.getElementById(`epic-features-${epicIdx}`);
        listDiv.innerHTML = '';

        epic.features.forEach((feat, fIdx) => {
            const item = document.createElement('div');
            item.className = 'onboard-list-item';
            item.innerHTML = `
                <input type="checkbox" class="item-checkbox feat-check" data-epic="${epicIdx}" data-feat="${fIdx}" ${feat.selected ? 'checked' : ''} />
                <div class="item-content">
                    <div class="item-title-row">
                        <h5 class="item-title">${escapeHtml(feat.name)}</h5>
                        <div class="item-actions">
                            <button class="btn-icon-action btn-edit" data-type="feature" data-epic="${epicIdx}" data-feat="${fIdx}"><i class="fas fa-edit"></i></button>
                            <button class="btn-icon-action btn-icon-delete btn-delete" data-type="feature" data-epic="${epicIdx}" data-feat="${fIdx}"><i class="fas fa-trash"></i></button>
                        </div>
                    </div>
                    <p class="item-desc">${escapeHtml(feat.description)}</p>
                </div>
            `;
            listDiv.appendChild(item);
        });

        // Listeners
        listDiv.querySelectorAll('.feat-check').forEach(cb => {
            cb.addEventListener('change', (e) => {
                const epicId = parseInt(e.target.dataset.epic);
                const featId = parseInt(e.target.dataset.feat);
                wizardState.epics[epicId].features[featId].selected = e.target.checked;
            });
        });

        listDiv.querySelectorAll('.btn-edit').forEach(btn => {
            btn.addEventListener('click', (e) => openEditModal(e.currentTarget));
        });

        listDiv.querySelectorAll('.btn-delete').forEach(btn => {
            btn.addEventListener('click', (e) => deleteItem(e.currentTarget));
        });
    }

    // ── STEP 4: User Stories Discovery ──────────────────────────────────────────
    async function loadStoriesStep() {
        const container = document.getElementById('stories-features-accordion');
        container.innerHTML = '';

        let hasSelectedFeatures = false;
        btnNext.disabled = true;

        for (let i = 0; i < wizardState.epics.length; i++) {
            const epic = wizardState.epics[i];
            if (!epic.selected) continue;

            for (let j = 0; j < epic.features.length; j++) {
                const feat = epic.features[j];
                if (!feat.selected) continue;

                hasSelectedFeatures = true;

                const card = document.createElement('div');
                card.className = 'glass-card mb-3';
                card.innerHTML = `
                    <div class="accordion-toggle d-flex justify-content-between align-items-center" data-bs-toggle="collapse" data-bs-target="#collapse-feat-${i}-${j}">
                        <h4 class="mb-0"><i class="fas fa-cube text-primary"></i> ${escapeHtml(feat.name)} <span class="text-muted small">(${escapeHtml(epic.name)})</span></h4>
                        <span class="accordion-arrow"><i class="fas fa-chevron-down"></i></span>
                    </div>
                    <div id="collapse-feat-${i}-${j}" class="collapse show mt-3">
                        <div class="stories-list" id="feat-stories-${i}-${j}">
                            <div class="onboard-loading-wrapper py-3">
                                <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
                                <span class="ms-2 small">Formulating user stories...</span>
                            </div>
                        </div>
                        <button class="btn btn-sm btn-outline-secondary floating-add-btn btn-add-story" data-epic="${i}" data-feat="${j}">
                            <i class="fas fa-plus"></i> Add User Story to ${escapeHtml(feat.name)}
                        </button>
                    </div>
                `;
                container.appendChild(card);

                // Fetch user stories if empty
                if (feat.userStories.length === 0) {
                    try {
                        const res = await fetch(`/api/onboard/analyze-stories/${feat.id}`, {
                            method: 'POST',
                            headers: {
                                'RequestVerificationToken': getAntiForgeryToken()
                            }
                        });

                        if (res.ok) {
                            const data = await res.json();
                            feat.userStories = data.map(s => ({
                                id: s.id,
                                title: s.title,
                                description: s.description,
                                acceptanceCriteria: s.acceptanceCriteria,
                                priority: s.priority,
                                selected: true,
                                tasks: [],
                                testCases: []
                            }));
                        }
                    } catch (err) {
                        console.error("Failed to suggest stories", feat.name, err);
                    }
                }

                renderStoriesList(i, j);
            }
        }

        if (!hasSelectedFeatures) {
            container.innerHTML = '<div class="alert alert-warning">No Features selected. Please select at least one Feature to generate User Stories.</div>';
            return;
        }

        btnNext.disabled = false;

        // Add story handler
        container.querySelectorAll('.btn-add-story').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const epicIdx = parseInt(e.currentTarget.dataset.epic);
                const featIdx = parseInt(e.currentTarget.dataset.feat);
                wizardState.epics[epicIdx].features[featIdx].userStories.push({
                    title: 'New User Story',
                    description: 'As a... I want to... So that...',
                    acceptanceCriteria: 'Given ... When ... Then ...',
                    priority: 'Medium',
                    selected: true,
                    tasks: [],
                    testCases: []
                });
                renderStoriesList(epicIdx, featIdx);
            });
        });
    }

    function renderStoriesList(epicIdx, featIdx) {
        const feat = wizardState.epics[epicIdx].features[featIdx];
        const listDiv = document.getElementById(`feat-stories-${epicIdx}-${featIdx}`);
        listDiv.innerHTML = '';

        feat.userStories.forEach((story, sIdx) => {
            const item = document.createElement('div');
            item.className = 'onboard-list-item';
            item.innerHTML = `
                <input type="checkbox" class="item-checkbox story-check" data-epic="${epicIdx}" data-feat="${featIdx}" data-story="${sIdx}" ${story.selected ? 'checked' : ''} />
                <div class="item-content">
                    <div class="item-title-row">
                        <h5 class="item-title">${escapeHtml(story.title)} <span class="badge bg-secondary ms-2 small">${story.priority}</span></h5>
                        <div class="item-actions">
                            <button class="btn-icon-action btn-edit" data-type="story" data-epic="${epicIdx}" data-feat="${featIdx}" data-story="${sIdx}"><i class="fas fa-edit"></i></button>
                            <button class="btn-icon-action btn-icon-delete btn-delete" data-type="story" data-epic="${epicIdx}" data-feat="${featIdx}" data-story="${sIdx}"><i class="fas fa-trash"></i></button>
                        </div>
                    </div>
                    <p class="item-desc mb-2">${escapeHtml(story.description)}</p>
                    <div class="bg-light p-2 rounded small text-muted font-monospace" style="white-space: pre-wrap;">${escapeHtml(story.acceptanceCriteria)}</div>
                </div>
            `;
            listDiv.appendChild(item);
        });

        // Listeners
        listDiv.querySelectorAll('.story-check').forEach(cb => {
            cb.addEventListener('change', (e) => {
                const epicId = parseInt(e.target.dataset.epic);
                const featId = parseInt(e.target.dataset.feat);
                const storyId = parseInt(e.target.dataset.story);
                wizardState.epics[epicId].features[featId].userStories[storyId].selected = e.target.checked;
            });
        });

        listDiv.querySelectorAll('.btn-edit').forEach(btn => {
            btn.addEventListener('click', (e) => openEditModal(e.currentTarget));
        });

        listDiv.querySelectorAll('.btn-delete').forEach(btn => {
            btn.addEventListener('click', (e) => deleteItem(e.currentTarget));
        });
    }

    // ── STEP 5: Tasks & Test Cases ───────────────────────────────────────────
    async function loadTasksStep() {
        const container = document.getElementById('tasks-stories-accordion');
        container.innerHTML = '';

        let hasSelectedStories = false;
        btnNext.disabled = true;

        for (let i = 0; i < wizardState.epics.length; i++) {
            const epic = wizardState.epics[i];
            if (!epic.selected) continue;

            for (let j = 0; j < epic.features.length; j++) {
                const feat = epic.features[j];
                if (!feat.selected) continue;

                for (let k = 0; k < feat.userStories.length; k++) {
                    const story = feat.userStories[k];
                    if (!story.selected) continue;

                    hasSelectedStories = true;

                    const card = document.createElement('div');
                    card.className = 'glass-card mb-3';
                    card.innerHTML = `
                        <div class="accordion-toggle d-flex justify-content-between align-items-center" data-bs-toggle="collapse" data-bs-target="#collapse-story-${i}-${j}-${k}">
                            <h5 class="mb-0"><i class="fas fa-file-alt text-primary"></i> ${escapeHtml(story.title)}</h5>
                            <span class="accordion-arrow"><i class="fas fa-chevron-down"></i></span>
                        </div>
                        <div id="collapse-story-${i}-${j}-${k}" class="collapse show mt-3">
                            <div class="row">
                                <div class="col-md-7 border-end">
                                    <h6>Suggested Tasks (PERT Estimates)</h6>
                                    <div class="tasks-list" id="story-tasks-${i}-${j}-${k}">
                                        <div class="onboard-loading-wrapper py-2">
                                            <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
                                        </div>
                                    </div>
                                    <button class="btn btn-sm btn-outline-secondary floating-add-btn btn-add-task mt-2" data-epic="${i}" data-feat="${j}" data-story="${k}">
                                        <i class="fas fa-plus"></i> Add Custom Task
                                    </button>
                                </div>
                                <div class="col-md-5 ps-4">
                                    <h6>Suggested QA Test Cases</h6>
                                    <div class="testcases-list" id="story-tcs-${i}-${j}-${k}">
                                        <div class="onboard-loading-wrapper py-2">
                                            <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
                                        </div>
                                    </div>
                                    <button class="btn btn-sm btn-outline-secondary floating-add-btn btn-add-tc mt-2" data-epic="${i}" data-feat="${j}" data-story="${k}">
                                        <i class="fas fa-plus"></i> Add Custom Test Case
                                    </button>
                                </div>
                            </div>
                        </div>
                    `;
                    container.appendChild(card);

                    // Fetch tasks & test cases if empty
                    if (story.tasks.length === 0 && story.testCases.length === 0) {
                        try {
                            const res = await fetch(`/api/onboard/analyze-tasks-tests/${story.id}`, {
                                method: 'POST',
                                headers: {
                                    'RequestVerificationToken': getAntiForgeryToken()
                                }
                            });

                            if (res.ok) {
                                const data = await res.json();
                                story.tasks = data.tasks.map(t => ({
                                    id: t.id,
                                    title: t.title,
                                    description: t.description,
                                    optimisticHours: t.optimisticHours,
                                    mostLikelyHours: t.mostLikelyHours,
                                    pessimisticHours: t.pessimisticHours,
                                    priority: t.priority,
                                    selected: true
                                }));

                                story.testCases = data.testCases.map(tc => ({
                                    id: tc.id,
                                    title: tc.title,
                                    steps: tc.steps,
                                    expectedResult: tc.expectedResult,
                                    selected: true
                                }));
                            }
                        } catch (err) {
                            console.error("Failed to suggest tasks/tests", story.title, err);
                        }
                    }

                    renderTasksAndTcsList(i, j, k);
                }
            }
        }

        if (!hasSelectedStories) {
            container.innerHTML = '<div class="alert alert-warning">No User Stories selected. Please select at least one User Story.</div>';
            return;
        }

        btnNext.disabled = false;

        // Custom Add Task / Test case listeners
        container.querySelectorAll('.btn-add-task').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const epicIdx = parseInt(e.currentTarget.dataset.epic);
                const featIdx = parseInt(e.currentTarget.dataset.feat);
                const storyIdx = parseInt(e.currentTarget.dataset.story);
                
                wizardState.epics[epicIdx].features[featIdx].userStories[storyIdx].tasks.push({
                    title: 'New Task',
                    description: 'Description of engineering work',
                    optimisticHours: 4,
                    mostLikelyHours: 8,
                    pessimisticHours: 16,
                    priority: 'Medium',
                    selected: true
                });
                renderTasksAndTcsList(epicIdx, featIdx, storyIdx);
            });
        });

        container.querySelectorAll('.btn-add-tc').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const epicIdx = parseInt(e.currentTarget.dataset.epic);
                const featIdx = parseInt(e.currentTarget.dataset.feat);
                const storyIdx = parseInt(e.currentTarget.dataset.story);
                
                wizardState.epics[epicIdx].features[featIdx].userStories[storyIdx].testCases.push({
                    title: 'New Test Case',
                    steps: '1. Open page\n2. Perform action',
                    expectedResult: 'Result is successful',
                    selected: true
                });
                renderTasksAndTcsList(epicIdx, featIdx, storyIdx);
            });
        });
    }

    function renderTasksAndTcsList(epicIdx, featIdx, storyIdx) {
        const story = wizardState.epics[epicIdx].features[featIdx].userStories[storyIdx];
        const tasksDiv = document.getElementById(`story-tasks-${epicIdx}-${featIdx}-${storyIdx}`);
        const tcsDiv = document.getElementById(`story-tcs-${epicIdx}-${featIdx}-${storyIdx}`);

        // 1. Render Tasks
        tasksDiv.innerHTML = '';
        story.tasks.forEach((task, tIdx) => {
            const item = document.createElement('div');
            item.className = 'onboard-list-item py-2';
            item.innerHTML = `
                <input type="checkbox" class="item-checkbox task-check" data-epic="${epicIdx}" data-feat="${featIdx}" data-story="${storyIdx}" data-task="${tIdx}" ${task.selected ? 'checked' : ''} />
                <div class="item-content">
                    <div class="item-title-row">
                        <h6 class="mb-0 text-dark small font-weight-bold">${escapeHtml(task.title)}</h6>
                        <div class="item-actions">
                            <button class="btn-icon-action btn-edit" data-type="task" data-epic="${epicIdx}" data-feat="${featIdx}" data-story="${storyIdx}" data-task="${tIdx}"><i class="fas fa-edit"></i></button>
                            <button class="btn-icon-action btn-icon-delete btn-delete" data-type="task" data-epic="${epicIdx}" data-feat="${featIdx}" data-story="${storyIdx}" data-task="${tIdx}"><i class="fas fa-trash"></i></button>
                        </div>
                    </div>
                    <div class="text-muted small mt-1">Estimates: O:${task.optimisticHours}h / M:${task.mostLikelyHours}h / P:${task.pessimisticHours}h</div>
                </div>
            `;
            tasksDiv.appendChild(item);
        });

        // 2. Render Test Cases
        tcsDiv.innerHTML = '';
        story.testCases.forEach((tc, tcIdx) => {
            const item = document.createElement('div');
            item.className = 'onboard-list-item py-2';
            item.innerHTML = `
                <input type="checkbox" class="item-checkbox tc-check" data-epic="${epicIdx}" data-feat="${featIdx}" data-story="${storyIdx}" data-tc="${tcIdx}" ${tc.selected ? 'checked' : ''} />
                <div class="item-content">
                    <div class="item-title-row">
                        <h6 class="mb-0 text-dark small font-weight-bold">${escapeHtml(tc.title)}</h6>
                        <div class="item-actions">
                            <button class="btn-icon-action btn-edit" data-type="tc" data-epic="${epicIdx}" data-feat="${featIdx}" data-story="${storyIdx}" data-tc="${tcIdx}"><i class="fas fa-edit"></i></button>
                            <button class="btn-icon-action btn-icon-delete btn-delete" data-type="tc" data-epic="${epicIdx}" data-feat="${featIdx}" data-story="${storyIdx}" data-tc="${tcIdx}"><i class="fas fa-trash"></i></button>
                        </div>
                    </div>
                </div>
            `;
            tcsDiv.appendChild(item);
        });

        // Checkbox events
        tasksDiv.querySelectorAll('.task-check').forEach(cb => {
            cb.addEventListener('change', (e) => {
                const epicId = parseInt(e.target.dataset.epic);
                const featId = parseInt(e.target.dataset.feat);
                const storyId = parseInt(e.target.dataset.story);
                const taskId = parseInt(e.target.dataset.task);
                wizardState.epics[epicId].features[featId].userStories[storyId].tasks[taskId].selected = e.target.checked;
            });
        });

        tcsDiv.querySelectorAll('.tc-check').forEach(cb => {
            cb.addEventListener('change', (e) => {
                const epicId = parseInt(e.target.dataset.epic);
                const featId = parseInt(e.target.dataset.feat);
                const storyId = parseInt(e.target.dataset.story);
                const tcId = parseInt(e.target.dataset.tc);
                wizardState.epics[epicId].features[featId].userStories[storyId].testCases[tcId].selected = e.target.checked;
            });
        });

        // Edit/Delete handlers
        [tasksDiv, tcsDiv].forEach(div => {
            div.querySelectorAll('.btn-edit').forEach(btn => {
                btn.addEventListener('click', (e) => openEditModal(e.currentTarget));
            });
            div.querySelectorAll('.btn-delete').forEach(btn => {
                btn.addEventListener('click', (e) => deleteItem(e.currentTarget));
            });
        });
    }

    // ── STEP 6: Review & Save ───────────────────────────────────────────────
    function loadReviewStep() {
        const container = document.getElementById('final-tree-container');
        container.innerHTML = '';

        const tree = buildFinalHierarchyTree();

        if (tree.Epics.length === 0) {
            container.innerHTML = '<div class="alert alert-danger">The onboarding structure contains no elements. Please go back and make selections.</div>';
            btnNext.disabled = true;
            return;
        }

        const rootUl = document.createElement('ul');
        rootUl.className = 'list-unstyled ps-0';

        tree.Epics.forEach(epic => {
            const epicLi = document.createElement('li');
            epicLi.className = 'mb-3';
            epicLi.innerHTML = `
                <div class="fw-bold"><i class="fas fa-folder text-primary me-2"></i> ${escapeHtml(epic.Name)}</div>
                <div class="text-muted small ms-4 mb-2">${escapeHtml(epic.Description || '')}</div>
            `;
            
            const featUl = document.createElement('ul');
            featUl.className = 'list-unstyled ms-4 border-left ps-3';

            epic.Features.forEach(feat => {
                const featLi = document.createElement('li');
                featLi.className = 'mb-2';
                featLi.innerHTML = `
                    <div class="fw-semibold"><i class="fas fa-cube text-info me-2"></i> ${escapeHtml(feat.Name)}</div>
                    <div class="text-muted small ms-4 mb-1">${escapeHtml(feat.Description || '')}</div>
                `;

                const storyUl = document.createElement('ul');
                storyUl.className = 'list-unstyled ms-4 border-left ps-3';

                feat.UserStories.forEach(story => {
                    const storyLi = document.createElement('li');
                    storyLi.className = 'mb-2';
                    storyLi.innerHTML = `
                        <div><i class="fas fa-file-alt text-warning me-2"></i> <strong>${escapeHtml(story.Title)}</strong> <span class="badge bg-secondary small ms-1">${story.Priority}</span></div>
                        <div class="ms-4 small text-muted">${escapeHtml(story.Description || '')}</div>
                    `;

                    // Render Tasks/Tests summary
                    if (story.Tasks.length > 0 || story.TestCases.length > 0) {
                        const itemsSummary = document.createElement('div');
                        itemsSummary.className = 'ms-4 mt-1 small text-muted';
                        itemsSummary.innerHTML = `
                            <span class="me-3"><i class="fas fa-tasks text-success"></i> ${story.Tasks.length} Task(s)</span>
                            <span><i class="fas fa-vial text-danger"></i> ${story.TestCases.length} Test Case(s)</span>
                        `;
                        storyLi.appendChild(itemsSummary);
                    }

                    storyUl.appendChild(storyLi);
                });

                featLi.appendChild(storyUl);
                featUl.appendChild(featLi);
            });

            epicLi.appendChild(featUl);
            rootUl.appendChild(epicLi);
        });

        container.appendChild(rootUl);
        btnNext.disabled = false;
        btnNext.innerHTML = '<i class="fas fa-check-circle"></i> Confirm & Initiate Project';
    }

    async function submitOnboarding() {
        btnNext.disabled = true;
        btnNext.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Initiating Project...';

        try {
            const res = await fetch(`/api/onboard/complete/${projectId}`, {
                method: 'POST',
                headers: {
                    'RequestVerificationToken': getAntiForgeryToken()
                }
            });

            if (!res.ok) {
                const errText = await res.text();
                throw new Error(errText);
            }

            // Redirect back to Project Details
            window.location.href = `/Projects/Details/${projectId}`;
        } catch (err) {
            btnNext.disabled = false;
            btnNext.innerHTML = '<i class="fas fa-check-circle"></i> Confirm & Initiate Project';
            alert(`Onboarding submission failed: ${err.message}`);
        }
    }

    // Filter tree to include selected elements only
    function buildFinalHierarchyTree() {
        const tree = {
            ProjectId: projectId,
            Epics: []
        };

        wizardState.epics.forEach(epic => {
            if (!epic.selected) return;

            const onboardEpic = {
                Name: epic.name,
                Description: epic.description,
                Features: []
            };

            epic.features.forEach(feat => {
                if (!feat.selected) return;

                const onboardFeat = {
                    Name: feat.name,
                    Description: feat.description,
                    UserStories: []
                };

                feat.userStories.forEach(story => {
                    if (!story.selected) return;

                    const onboardStory = {
                        Title: story.title,
                        Description: story.description,
                        AcceptanceCriteria: story.acceptanceCriteria,
                        Priority: story.priority,
                        Tasks: [],
                        TestCases: []
                    };

                    story.tasks.forEach(task => {
                        if (!task.selected) return;
                        onboardStory.Tasks.push({
                            Title: task.title,
                            Description: task.description,
                            Priority: task.priority,
                            OptimisticHours: task.optimisticHours,
                            MostLikelyHours: task.mostLikelyHours,
                            PessimisticHours: task.pessimisticHours
                        });
                    });

                    story.testCases.forEach(tc => {
                        if (!tc.selected) return;
                        onboardStory.TestCases.push({
                            Title: tc.title,
                            Steps: tc.steps,
                            ExpectedResult: tc.expectedResult
                        });
                    });

                    onboardFeat.UserStories.push(onboardStory);
                });

                onboardEpic.Features.push(onboardFeat);
            });

            tree.Epics.push(onboardEpic);
        });

        return tree;
    }

    // ── EDIT MODAL LOGIC ─────────────────────────────────────────────────────
    function openEditModal(btn) {
        if (!editModal) return;

        const type = btn.dataset.type;
        const eIdx = parseInt(btn.dataset.epic ?? '-1');
        const fIdx = parseInt(btn.dataset.feat ?? '-1');
        const sIdx = parseInt(btn.dataset.story ?? '-1');
        const tIdx = parseInt(btn.dataset.task ?? '-1');
        const tcIdx = parseInt(btn.dataset.tc ?? '-1');

        document.getElementById('edit-item-type').value = type;
        document.getElementById('edit-epic-idx').value = eIdx;
        document.getElementById('edit-feat-idx').value = fIdx;
        document.getElementById('edit-story-idx').value = sIdx;
        document.getElementById('edit-task-idx').value = tIdx;
        document.getElementById('edit-tc-idx').value = tcIdx;

        // Reset display groupings
        document.getElementById('edit-desc-group').style.display = 'block';
        document.getElementById('edit-ac-group').style.display = 'none';
        document.getElementById('edit-priority-group').style.display = 'none';
        document.getElementById('edit-hours-group').style.display = 'none';
        document.getElementById('edit-tc-group').style.display = 'none';

        document.getElementById('lbl-edit-title').textContent = 'Title / Name';

        let item = null;

        if (type === 'epic') {
            item = wizardState.epics[eIdx];
            document.getElementById('edit-title').value = item.name;
            document.getElementById('edit-desc').value = item.description;
        } else if (type === 'feature') {
            item = wizardState.epics[eIdx].features[fIdx];
            document.getElementById('edit-title').value = item.name;
            document.getElementById('edit-desc').value = item.description;
        } else if (type === 'story') {
            item = wizardState.epics[eIdx].features[fIdx].userStories[sIdx];
            document.getElementById('edit-title').value = item.title;
            document.getElementById('edit-desc').value = item.description;
            
            document.getElementById('edit-ac-group').style.display = 'block';
            document.getElementById('edit-priority-group').style.display = 'block';
            document.getElementById('edit-ac').value = item.acceptanceCriteria;
            document.getElementById('edit-priority').value = item.priority;
        } else if (type === 'task') {
            item = wizardState.epics[eIdx].features[fIdx].userStories[sIdx].tasks[tIdx];
            document.getElementById('edit-title').value = item.title;
            document.getElementById('edit-desc').value = item.description;
            
            document.getElementById('edit-priority-group').style.display = 'block';
            document.getElementById('edit-hours-group').style.display = 'flex';
            document.getElementById('edit-priority').value = item.priority;
            document.getElementById('edit-opt-hours').value = item.optimisticHours;
            document.getElementById('edit-ml-hours').value = item.mostLikelyHours;
            document.getElementById('edit-pess-hours').value = item.pessimisticHours;
        } else if (type === 'tc') {
            item = wizardState.epics[eIdx].features[fIdx].userStories[sIdx].testCases[tcIdx];
            document.getElementById('edit-title').value = item.title;
            document.getElementById('edit-desc-group').style.display = 'none';
            
            document.getElementById('edit-tc-group').style.display = 'block';
            document.getElementById('edit-tc-steps').value = item.steps;
            document.getElementById('edit-tc-result').value = item.expectedResult;
        }

        editModal.show();
    }

    document.getElementById('btn-save-edit-item')?.addEventListener('click', () => {
        const type = document.getElementById('edit-item-type').value;
        const eIdx = parseInt(document.getElementById('edit-epic-idx').value);
        const fIdx = parseInt(document.getElementById('edit-feat-idx').value);
        const sIdx = parseInt(document.getElementById('edit-story-idx').value);
        const tIdx = parseInt(document.getElementById('edit-task-idx').value);
        const tcIdx = parseInt(document.getElementById('edit-tc-idx').value);

        const titleVal = document.getElementById('edit-title').value.trim();
        const descVal = document.getElementById('edit-desc').value.trim();

        if (!titleVal) return;

        if (type === 'epic') {
            wizardState.epics[eIdx].name = titleVal;
            wizardState.epics[eIdx].description = descVal;
            renderEpicsList();
        } else if (type === 'feature') {
            wizardState.epics[eIdx].features[fIdx].name = titleVal;
            wizardState.epics[eIdx].features[fIdx].description = descVal;
            renderFeaturesList(eIdx);
        } else if (type === 'story') {
            const story = wizardState.epics[eIdx].features[fIdx].userStories[sIdx];
            story.title = titleVal;
            story.description = descVal;
            story.acceptanceCriteria = document.getElementById('edit-ac').value.trim();
            story.priority = document.getElementById('edit-priority').value;
            renderStoriesList(eIdx, fIdx);
        } else if (type === 'task') {
            const task = wizardState.epics[eIdx].features[fIdx].userStories[sIdx].tasks[tIdx];
            task.title = titleVal;
            task.description = descVal;
            task.priority = document.getElementById('edit-priority').value;
            task.optimisticHours = parseFloat(document.getElementById('edit-opt-hours').value) || 0;
            task.mostLikelyHours = parseFloat(document.getElementById('edit-ml-hours').value) || 0;
            task.pessimisticHours = parseFloat(document.getElementById('edit-pess-hours').value) || 0;
            renderTasksAndTcsList(eIdx, fIdx, sIdx);
        } else if (type === 'tc') {
            const tc = wizardState.epics[eIdx].features[fIdx].userStories[sIdx].testCases[tcIdx];
            tc.title = titleVal;
            tc.steps = document.getElementById('edit-tc-steps').value.trim();
            tc.expectedResult = document.getElementById('edit-tc-result').value.trim();
            renderTasksAndTcsList(eIdx, fIdx, sIdx);
        }

        if (editModal) editModal.hide();
    });

    function deleteItem(btn) {
        if (!confirm("Are you sure you want to remove this item?")) return;

        const type = btn.dataset.type;
        const eIdx = parseInt(btn.dataset.epic ?? '-1');
        const fIdx = parseInt(btn.dataset.feat ?? '-1');
        const sIdx = parseInt(btn.dataset.story ?? '-1');
        const tIdx = parseInt(btn.dataset.task ?? '-1');
        const tcIdx = parseInt(btn.dataset.tc ?? '-1');

        if (type === 'epic') {
            wizardState.epics.splice(eIdx, 1);
            renderEpicsList();
        } else if (type === 'feature') {
            wizardState.epics[eIdx].features.splice(fIdx, 1);
            renderFeaturesList(eIdx);
        } else if (type === 'story') {
            wizardState.epics[eIdx].features[fIdx].userStories.splice(sIdx, 1);
            renderStoriesList(eIdx, fIdx);
        } else if (type === 'task') {
            wizardState.epics[eIdx].features[fIdx].userStories[sIdx].tasks.splice(tIdx, 1);
            renderTasksAndTcsList(eIdx, fIdx, sIdx);
        } else if (type === 'tc') {
            wizardState.epics[eIdx].features[fIdx].userStories[sIdx].testCases.splice(tcIdx, 1);
            renderTasksAndTcsList(eIdx, fIdx, sIdx);
        }
    }
    // ── STEP-BY-STEP PERSISTENCE FUNCTIONS ──────────────────────────────────────
    async function saveEpicsStep() {
        const selectedEpics = wizardState.epics.filter(e => e.selected);
        const body = {
            projectId: projectId,
            epics: selectedEpics.map(e => ({
                id: e.id || null,
                name: e.name,
                description: e.description
            }))
        };

        const res = await fetch('/api/onboard/save-epics', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify(body)
        });

        if (!res.ok) {
            throw new Error(await res.text());
        }

        const savedEpics = await res.json();
        let savedIdx = 0;
        for (let i = 0; i < wizardState.epics.length; i++) {
            if (wizardState.epics[i].selected) {
                const saved = savedEpics[savedIdx++];
                wizardState.epics[i].id = saved.id;
                if (!wizardState.epics[i].features) {
                    wizardState.epics[i].features = [];
                }
            }
        }
        wizardState.epics = wizardState.epics.filter(e => e.selected);
    }

    async function saveFeaturesStep() {
        for (const epic of wizardState.epics) {
            if (!epic.selected || !epic.id) continue;

            const selectedFeatures = (epic.features || []).filter(f => f.selected);
            const body = {
                epicId: epic.id,
                features: selectedFeatures.map(f => ({
                    id: f.id || null,
                    name: f.name,
                    description: f.description
                }))
            };

            const res = await fetch('/api/onboard/save-features', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getAntiForgeryToken()
                },
                body: JSON.stringify(body)
            });

            if (!res.ok) {
                throw new Error(`Failed to save features for epic ${epic.name}: ${await res.text()}`);
            }

            const savedFeatures = await res.json();
            let savedIdx = 0;
            for (let i = 0; i < epic.features.length; i++) {
                if (epic.features[i].selected) {
                    const saved = savedFeatures[savedIdx++];
                    epic.features[i].id = saved.id;
                    if (!epic.features[i].userStories) {
                        epic.features[i].userStories = [];
                    }
                }
            }
            epic.features = epic.features.filter(f => f.selected);
        }
    }

    async function saveStoriesStep() {
        for (const epic of wizardState.epics) {
            if (!epic.selected) continue;
            for (const feat of epic.features) {
                if (!feat.selected || !feat.id) continue;

                const selectedStories = (feat.userStories || []).filter(s => s.selected);
                const body = {
                    featureId: feat.id,
                    stories: selectedStories.map(s => ({
                        id: s.id || null,
                        title: s.title,
                        description: s.description,
                        acceptanceCriteria: s.acceptanceCriteria,
                        priority: s.priority
                    }))
                };

                const res = await fetch('/api/onboard/save-stories', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': getAntiForgeryToken()
                    },
                    body: JSON.stringify(body)
                });

                if (!res.ok) {
                    throw new Error(`Failed to save stories for feature ${feat.name}: ${await res.text()}`);
                }

                const savedStories = await res.json();
                let savedIdx = 0;
                for (let i = 0; i < feat.userStories.length; i++) {
                    if (feat.userStories[i].selected) {
                        const saved = savedStories[savedIdx++];
                        feat.userStories[i].id = saved.id;
                        if (!feat.userStories[i].tasks) {
                            feat.userStories[i].tasks = [];
                        }
                        if (!feat.userStories[i].testCases) {
                            feat.userStories[i].testCases = [];
                        }
                    }
                }
                feat.userStories = feat.userStories.filter(s => s.selected);
            }
        }
    }

    async function saveTasksStep() {
        for (const epic of wizardState.epics) {
            if (!epic.selected) continue;
            for (const feat of epic.features) {
                if (!feat.selected) continue;
                for (const story of feat.userStories) {
                    if (!story.selected || !story.id) continue;

                    const selectedTasks = (story.tasks || []).filter(t => t.selected);
                    const selectedTests = (story.testCases || []).filter(tc => tc.selected);

                    const body = {
                        storyId: story.id,
                        tasks: selectedTasks.map(t => ({
                            id: t.id || null,
                            title: t.title,
                            description: t.description,
                            priority: t.priority,
                            optimisticHours: parseFloat(t.optimisticHours) || 0,
                            mostLikelyHours: parseFloat(t.mostLikelyHours) || 0,
                            pessimisticHours: parseFloat(t.pessimisticHours) || 0
                        })),
                        testCases: selectedTests.map(tc => ({
                            id: tc.id || null,
                            title: tc.title,
                            steps: tc.steps,
                            expectedResult: tc.expectedResult
                        }))
                    };

                    const res = await fetch('/api/onboard/save-tasks-tests', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'RequestVerificationToken': getAntiForgeryToken()
                        },
                        body: JSON.stringify(body)
                    });

                    if (!res.ok) {
                        throw new Error(`Failed to save tasks/tests for story ${story.title}: ${await res.text()}`);
                    }

                    const saved = await res.json();
                    let savedTaskIdx = 0;
                    for (let i = 0; i < story.tasks.length; i++) {
                        if (story.tasks[i].selected) {
                            story.tasks[i].id = saved.tasks[savedTaskIdx++].id;
                        }
                    }
                    story.tasks = story.tasks.filter(t => t.selected);

                    let savedTestIdx = 0;
                    for (let i = 0; i < story.testCases.length; i++) {
                        if (story.testCases[i].selected) {
                            story.testCases[i].id = saved.testCases[savedTestIdx++].id;
                        }
                    }
                    story.testCases = story.testCases.filter(tc => tc.selected);
                }
            }
        }
    }

    // ── STEPPER NAVIGATION DRIVER ─────────────────────────────────────────────
    btnNext.addEventListener('click', async () => {
        if (currentStep === maxStep) {
            await submitOnboarding();
            return;
        }

        btnNext.disabled = true;
        const originalText = btnNext.innerHTML;
        btnNext.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Saving Step...';

        try {
            if (currentStep === 2) {
                await saveEpicsStep();
            } else if (currentStep === 3) {
                await saveFeaturesStep();
            } else if (currentStep === 4) {
                await saveStoriesStep();
            } else if (currentStep === 5) {
                await saveTasksStep();
            }

            const nextStep = currentStep + 1;
            if (await transitionToStep(nextStep)) {
                currentStep = nextStep;
                updateStepNav();
            }
        } catch (err) {
            alert(`Failed to save step: ${err.message}`);
        } finally {
            btnNext.disabled = false;
            btnNext.innerHTML = originalText;
        }
    });
    btnPrev.addEventListener('click', async () => {
        if (currentStep === 1) return;

        const prevStep = currentStep - 1;
        if (await transitionToStep(prevStep)) {
            currentStep = prevStep;
            updateStepNav();
        }
    });

    async function transitionToStep(step) {
        // Run Tab Loaders
        if (step === 2) {
            await loadEpicsStep();
        } else if (step === 3) {
            await loadFeaturesStep();
        } else if (step === 4) {
            await loadStoriesStep();
        } else if (step === 5) {
            await loadTasksStep();
        } else if (step === 6) {
            loadReviewStep();
        }

        // Hide current, show next
        document.querySelectorAll('.wizard-panel').forEach(p => p.classList.remove('active'));
        document.getElementById(`panel-${step}`).classList.add('active');

        return true;
    }

    function updateStepNav() {
        // Stepper dots
        document.querySelectorAll('.wizard-step-node').forEach(node => {
            const stepNum = parseInt(node.dataset.step);
            node.classList.remove('active', 'completed');
            if (stepNum === currentStep) {
                node.classList.add('active');
            } else if (stepNum < currentStep) {
                node.classList.add('completed');
            }
        });

        // Prev/Next buttons
        btnPrev.disabled = currentStep === 1;
        
        if (currentStep === maxStep) {
            btnNext.innerHTML = '<i class="fas fa-check-circle"></i> Confirm & Initiate Project';
            btnNext.className = "btn btn-success";
        } else {
            btnNext.innerHTML = 'Next Step <i class="fas fa-chevron-right"></i>';
            btnNext.className = "btn btn-primary";
        }

        // Progress line fill width
        const pct = ((currentStep - 1) / (maxStep - 1)) * 100;
        progressBar.style.width = `${pct}%`;
    }

    // ── HELPERS ──────────────────────────────────────────────────────────────
    function getAntiForgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    }

    function showInlineLoader(parentEl, message) {
        const loader = document.createElement('div');
        loader.className = 'onboard-loading-wrapper py-5';
        loader.innerHTML = `
            <div class="onboard-spinner"></div>
            <h5 class="text-muted mt-2">${message}</h5>
        `;
        parentEl.appendChild(loader);
        return loader;
    }

    function escapeHtml(str) {
        if (!str) return '';
        return str
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }
})();
