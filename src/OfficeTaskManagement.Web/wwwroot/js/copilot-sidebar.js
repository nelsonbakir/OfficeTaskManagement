/**
 * copilot-sidebar.js — AI Copilot Multi-turn Sidebar
 * Phase 4: Persistent AI sidebar with natural language → autonomous PM actions.
 * Spec: ai-agent-plan/07_FRONTEND_UX.md → FLOW 7
 */
(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', () => {
        // ── DOM refs ──────────────────────────────────────────────────────────────
        const sidebar      = document.getElementById('ai-copilot-sidebar');
        const toggleBtn    = document.getElementById('copilot-toggle-btn');
        const closeBtn     = document.getElementById('copilot-close-btn');
        const clearBtn     = document.getElementById('copilot-clear-btn');
        const messagesEl   = document.getElementById('copilot-messages');
        const actionsEl    = document.getElementById('copilot-actions');
        const inputEl      = document.getElementById('copilot-input');
        const sendBtn      = document.getElementById('copilot-send-btn');
        const charCountEl  = document.getElementById('copilot-char-count');
        const sessionBar      = document.getElementById('copilot-session-bar');
        const sessionSelector = document.getElementById('copilot-session-selector');
        const newSessionBtn   = document.getElementById('copilot-new-session-btn');

        const failedJobsPanel  = document.getElementById('copilot-failed-jobs-panel');
        const failedJobsCount  = document.getElementById('failed-jobs-count');
        const failedJobsToggle = document.getElementById('failed-jobs-toggle');
        const failedJobsList   = document.getElementById('failed-jobs-list');

        if (!sidebar || !toggleBtn) return;

        // ── Mentions State ────────────────────────────────────────────────────────
        let mentions = []; // { type, id, label }
        let dropdownVisible = false;
        let activeDropdownIndex = -1;
        let dropdownItems = [];
        let searchTimeout = null;
        let activeTrigger = null; // null | '@' | '/'
        const projectSelector = document.getElementById('copilot-project-selector');

        // ── Create Backdrop and Dropdown ──────────────────────────────────────────
        const textareaContainer = inputEl.parentElement;
        const inputWrapper = inputEl.closest('.copilot-sidebar__input-wrapper');
        
        const backdropEl = document.createElement('div');
        backdropEl.className = 'copilot-sidebar__input-backdrop';
        textareaContainer.insertBefore(backdropEl, inputEl);

        const dropdownEl = document.createElement('div');
        dropdownEl.className = 'copilot-mention-dropdown';
        dropdownEl.style.display = 'none';
        inputWrapper.appendChild(dropdownEl);

        // ── Auto-resize Textarea and Backdrop ──────────────────────────────────────
        function autoResizeTextarea() {
            inputEl.style.height = 'auto';
            const minHeight = 38; // Default CSS min-height
            const maxHeight = 160; // Max height before scrolling
            
            let newHeight = inputEl.scrollHeight;
            if (newHeight < minHeight) newHeight = minHeight;
            if (newHeight > maxHeight) {
                newHeight = maxHeight;
                inputEl.style.overflowY = 'auto';
                backdropEl.style.overflowY = 'auto';
            } else {
                inputEl.style.overflowY = 'hidden';
                backdropEl.style.overflowY = 'hidden';
            }
            
            const heightStr = `${newHeight}px`;
            inputEl.style.height = heightStr;
            backdropEl.style.height = heightStr;
            
            // Keep scroll synchronized immediately
            backdropEl.scrollTop = inputEl.scrollTop;
        }

        // Keep backdrop scroll in sync with textarea scroll
        inputEl.addEventListener('scroll', () => {
            backdropEl.scrollTop = inputEl.scrollTop;
            backdropEl.scrollLeft = inputEl.scrollLeft;
        });

        // Initialize height
        autoResizeTextarea();

        // ── State ─────────────────────────────────────────────────────────────────
        // Read page context from meta tags injected by the layout for entity pages
        const entityType = document.querySelector('meta[name="ai-entity-type"]')?.content ?? null;
        const entityId   = document.querySelector('meta[name="ai-entity-id"]')?.content ?? null;

        // Unique conversation ID per browser session (persisted in sessionStorage)
        let conversationId = sessionStorage.getItem('copilot-conv-id') ?? generateConvId();
        sessionStorage.setItem('copilot-conv-id', conversationId);

        let isOpen    = false;
        let isSending = false;

        async function initProjectSelector() {
            if (!projectSelector) return;
            try {
                const resp = await fetch('/api/agent/user-projects');
                if (resp.ok) {
                    const projects = await resp.json();
                    projectSelector.innerHTML = '<option value="">-- Project Context --</option>';
                    projects.forEach(p => {
                        const opt = document.createElement('option');
                        opt.value = p.id;
                        opt.textContent = p.name;
                        projectSelector.appendChild(opt);
                    });
                    resolveActiveContext();
                    if (projectSelector.value) {
                        await refreshSessions(conversationId, true);
                    }
                } else {
                    console.warn(`Failed to fetch user projects: status ${resp.status} - ${resp.statusText}`);
                }
            } catch (err) {
                console.error("Failed to load user projects for context selector:", err);
            }
        }

        function resolveActiveContext() {
            if (!projectSelector) return;
            const pageProjId = document.querySelector('meta[name="ai-project-id"]')?.content;
            if (entityType === 'Project' && entityId) {
                projectSelector.value = entityId;
                projectSelector.disabled = true;
                projectSelector.title = "Locked to current page context";
            } else if (pageProjId) {
                projectSelector.value = pageProjId;
                projectSelector.disabled = true;
                projectSelector.title = "Locked to current page context";
            } else {
                projectSelector.disabled = false;
                projectSelector.removeAttribute('title');
                const savedProjId = sessionStorage.getItem('copilot-active-project-id');
                if (savedProjId) {
                    projectSelector.value = savedProjId;
                }
            }
        }

        if (projectSelector) {
            projectSelector.addEventListener('change', () => {
                sessionStorage.setItem('copilot-active-project-id', projectSelector.value);
                refreshSessions();
                loadFailedJobs();
            });
        }

        if (sessionSelector) {
            sessionSelector.addEventListener('change', () => {
                switchSession(sessionSelector.value);
            });
        }

        if (newSessionBtn) {
            newSessionBtn.addEventListener('click', () => {
                startNewSession();
            });
        }

        function getActiveProjectContextId() {
            if (projectSelector && projectSelector.value) {
                return parseInt(projectSelector.value);
            }
            return null;
        }

        // Initialize project list context
        initProjectSelector();
        loadFailedJobs();

        // Wire quick-action buttons
        document.querySelectorAll('.copilot-quick-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const cmd = btn.dataset.command;
                if (cmd) {
                    inputEl.value = cmd + ' ';
                    charCountEl.textContent = `${inputEl.value.length}/2000`;
                    sendBtn.disabled = false;
                    if (!isOpen) open();
                    inputEl.focus();
                    inputEl.setSelectionRange(inputEl.value.length, inputEl.value.length);
                    onInput();
                }
            });
        });

        // ── Open / Close ──────────────────────────────────────────────────────────
        toggleBtn.addEventListener('click', () => open());
        closeBtn.addEventListener('click',  () => close());

        document.addEventListener('keydown', e => {
            if (e.key === 'Escape' && isOpen) close();
        });

        function open() {
            isOpen = true;
            sidebar.classList.add('copilot-sidebar--open');
            sidebar.setAttribute('aria-hidden', 'false');
            toggleBtn.setAttribute('aria-expanded', 'true');
            toggleBtn.classList.add('copilot-toggle-btn--hidden');
            inputEl.focus();
        }

        function close() {
            isOpen = false;
            sidebar.classList.remove('copilot-sidebar--open');
            sidebar.setAttribute('aria-hidden', 'true');
            toggleBtn.setAttribute('aria-expanded', 'false');
            toggleBtn.classList.remove('copilot-toggle-btn--hidden');
            toggleBtn.focus();
        }

        // ── Input handling ────────────────────────────────────────────────────────
        inputEl.addEventListener('input', () => {
            const len = inputEl.value.length;
            charCountEl.textContent = `${len}/2000`;
            sendBtn.disabled = len === 0 || isSending;
            onInput();
        });

        inputEl.addEventListener('keydown', e => {
            if (dropdownVisible) {
                if (e.key === 'ArrowDown') {
                    e.preventDefault();
                    navigateDropdown(1);
                } else if (e.key === 'ArrowUp') {
                    e.preventDefault();
                    navigateDropdown(-1);
                } else if (e.key === 'Enter' || e.key === 'Tab') {
                    e.preventDefault();
                    selectActiveDropdownItem();
                } else if (e.key === 'Escape') {
                    e.preventDefault();
                    hideDropdown();
                }
            } else {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    if (!sendBtn.disabled) sendMessage();
                }
            }
        });

        sendBtn.addEventListener('click', () => sendMessage());

        // ── Clear conversation ────────────────────────────────────────────────────
        clearBtn.addEventListener('click', async () => {
            if (!confirm('Clear this conversation? This cannot be undone.')) return;
            try {
                await fetch(`/api/agent/conversation/${conversationId}`, {
                    method: 'DELETE',
                    headers: { 'RequestVerificationToken': getAntiForgeryToken() }
                });
            } catch { /* silent — local state still resets */ }

            await refreshSessions();
        });

        // ── Send message ──────────────────────────────────────────────────────
        async function sendMessage() {
            const text = inputEl.value.trim();
            if (!text || isSending) return;

            if (text.startsWith('/')) {
                handleSlashCommand(text);
                inputEl.value = '';
                charCountEl.textContent = '0/2000';
                sendBtn.disabled = true;
                updateBackdrop();
                return;
            }

            appendMessage('user', text);
            inputEl.value = '';
            charCountEl.textContent = '0/2000';
            sendBtn.disabled = true;
            updateBackdrop();
            await sendAiMessage(text);
        }

        async function sendAiMessage(text, displayMessage = null) {
            if (!text.trim() || isSending) return;
            isSending = true;
            sendBtn.disabled = true;
            const originalPayload = { text, displayMessage };
            const typingEl = appendTypingIndicator();

            try {
                // Try streaming endpoint first
                const streamResp = await fetch('/api/agent/chat/stream', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': getAntiForgeryToken()
                    },
                    body: JSON.stringify({
                        conversationId,
                        userId: '',
                        message: text,
                        entityType: entityType,
                        entityId: entityId ? parseInt(entityId) : null,
                        mentions: mentions.map(m => ({ type: m.type, id: m.id })),
                        projectContextId: getActiveProjectContextId(),
                        displayMessage: displayMessage
                    })
                });

                typingEl.remove();
                mentions = [];
                updateBackdrop();

                if (!streamResp.ok) {
                    const err = await streamResp.text();
                    throw new Error(err || 'AI service unavailable.');
                }

                // Stream the response token by token
                const aiMsgDiv = appendMessage('ai', '');
                const bubble = aiMsgDiv.querySelector('.copilot-msg__bubble');
                let fullText = '';

                const reader = streamResp.body.getReader();
                const decoder = new TextDecoder();
                let buffer = '';
                let actions = null;

                while (true) {
                    const { done, value } = await reader.read();
                    if (done) break;
                    buffer += decoder.decode(value, { stream: true });
                    const lines = buffer.split('\n');
                    buffer = lines.pop(); // keep incomplete last line

                    for (const line of lines) {
                        if (!line.trim()) continue;
                        let obj = null;
                        try {
                            obj = JSON.parse(line);
                        } catch { 
                            /* skip malformed chunk */ 
                            continue;
                        }

                        if (obj.done) break;
                        if (obj.error) {
                            throw new Error(obj.error);
                        }
                        if (obj.chunk) {
                            fullText += obj.chunk;
                            bubble.innerHTML = renderMarkdown(fullText);
                            messagesEl.scrollTop = messagesEl.scrollHeight;
                        }
                        if (obj.actions) {
                            actions = obj.actions;
                            const draftAction = actions.find(a => a.actionType.startsWith('draft_'));
                            if (draftAction) {
                                launchWbsWizard(draftAction);
                            }
                        }
                    }
                }

                // Final render
                const wbsData = tryParseWbs(fullText);
                if (wbsData) {
                    bubble.innerHTML = renderWbsProposedCard(wbsData);
                } else {
                    bubble.innerHTML = renderMarkdown(fullText || '(no response)');
                }
                
                if (fullText && (fullText.includes('# 📋 PM Status Report') || fullText.includes('PM Status Report'))) {
                    const projectId = getActiveProjectContextId();
                    if (projectId) {
                        const downloadDiv = document.createElement('div');
                        downloadDiv.className = 'copilot-msg__download-pdf';
                        downloadDiv.style.marginTop = '0.5rem';
                        downloadDiv.innerHTML = `
                            <a href="/api/pmreport/download/${projectId}" class="copilot-action-btn pdf-download-btn" target="_blank" style="text-decoration:none;">
                                📥 Download Report as PDF
                            </a>`;
                        bubble.appendChild(downloadDiv);
                    }
                }
                messagesEl.scrollTop = messagesEl.scrollHeight;

                // Refresh sessions if it was a new unsaved session
                if (sessionSelector) {
                    const activeOpt = sessionSelector.options[sessionSelector.selectedIndex];
                    const isNewSession = (activeOpt && activeOpt.textContent === '-- New Session --') || 
                                         !Array.from(sessionSelector.options).some(opt => opt.value === conversationId && opt.textContent !== '-- New Session --');
                    if (isNewSession) {
                        await refreshSessions(conversationId);
                    }
                }

                if (actions?.length) {
                    renderActions(actions);
                } else {
                    actionsEl.style.display = 'none';
                }
                
                await loadFailedJobs();
            } catch (err) {
                typingEl?.remove();
                appendErrorMessage(err.message, text);
                await loadFailedJobs();
            } finally {
                isSending = false;
                sendBtn.disabled = inputEl.value.length === 0;
            }
        }

        function handleSlashCommand(cmdText) {
            const parts = cmdText.split(' ');
            const cmd = parts[0].toLowerCase();
            const args = parts.slice(1).join(' ');
            appendMessage('user', cmdText);

            // Local-only commands (no AI call needed)
            switch (cmd) {
                case '/capacity':
                    appendMessage('ai', '🔄 Redirecting to Capacity Planning Dashboard...');
                    setTimeout(() => window.location.href = '/Capacity', 1000);
                    return;
                case '/pert':
                    appendMessage('ai', `**PERT Three-Point Estimation**\n\nFormula: **(O + 4M + P) / 6**\n\n- **O** — Optimistic (best-case)\n- **M** — Most Likely (normal)\n- **P** — Pessimistic (worst-case)\n\nStandard Deviation: σ = (P − O) / 6\n\nOfficeTaskManagement automatically applies PERT to all AI estimates.`);
                    return;
                case '/reestimate':
                    appendMessage('ai', `**Bulk Re-estimation**\n\n1. Go to the Tasks list page\n2. Select tasks with checkboxes\n3. Click **Re-estimate Selected Tasks with AI**\n4. Review and confirm`);
                    return;
            }

            // AI-powered commands — send as a structured message to the backend
            let aiMessage = '';
            switch (cmd) {
                case '/help':
                    aiMessage = 'List all your capabilities and available slash commands with examples for each.';
                    break;
                case '/plan':
                    aiMessage = args
                        ? `Parse these meeting notes and create a project Work Breakdown Structure. Use the draft_epics tool to start the interactive WBS planning flow. Wait for user approval before moving to the next level. Meeting notes:\n\n${args}`
                        : 'I want to create a project plan from meeting notes. Please ask me to paste my meeting notes and then use draft_epics to begin the interactive WBS planning flow.';
                    break;
                case '/report':
                    aiMessage = args
                        ? `Generate a comprehensive PMP-grade status report for project: ${args}. Include executive summary (RAG status), sprint progress, risks, resource utilization, and recommendations.`
                        : 'Generate a comprehensive PMP-grade status report for the active project. Read project status, sprint capacity, and task data using read_project_status and read_sprint_list tools. Include executive summary (RAG: 🟢/🟡/🔴), sprint velocity, risks, and next steps.';
                    break;
                case '/risk':
                    aiMessage = args
                        ? `Analyze all risks for project: ${args}. Use read_project_status and read_project_tasks to find: stale tasks (>5 days In Progress), overloaded resources, tasks with no estimates, and sprint overloads. Present as a risk register table.`
                        : 'Analyze the active project for risks. Use read_project_status and read_project_tasks to find: stale tasks (In Progress >5 days), missing estimates, sprint overloads, and resource bottlenecks. Present as a prioritized risk register.';
                    break;
                case '/sprint':
                    aiMessage = args
                        ? `Plan sprint ${args} intelligently: use get_sprint_capacity and read_project_tasks to see available backlog, then recommend which tasks to assign. Explain your reasoning based on team capacity and task priorities.`
                        : 'Help me plan the current sprint. Use get_sprint_capacity to check available hours, then use read_project_tasks to find unassigned backlog items. Recommend the best tasks to pull into the sprint based on priority and PERT estimates.';
                    break;
                case '/standup':
                    aiMessage = 'Generate a daily standup digest using read_project_tasks filtered to my assigned tasks. Format: **Yesterday** (Done tasks), **Today** (In Progress/ToDo tasks), **Blockers** (any blocked or stale tasks).';
                    break;
                case '/testcases':
                    aiMessage = args
                        ? `Generate comprehensive BDD test cases for user story: ${args}. Include happy path, edge cases, and failure scenarios in Given/When/Then format.`
                        : 'I want to generate test cases. Please tell me which user story you want test cases for (provide the story title or @mention it).';
                    break;
                case '/estimate':
                    aiMessage = args
                        ? `Give me a PERT three-point estimate (Optimistic, Most Likely, Pessimistic hours) for this task: "${args}". Consider complexity, risks, and typical development patterns. Provide rationale.`
                        : 'What task or feature would you like me to estimate? Provide a description and I will give you a PERT three-point estimate.';
                    break;
                case '/read':
                    aiMessage = args
                        ? `Read and summarize: ${args}. Use read_project_tasks, read_sprint_list, or read_project_status as needed.`
                        : 'What would you like me to read? You can say: tasks in sprint 5, project status, backlog items, etc.';
                    break;
                default:
                    appendMessage('ai', `⚠ Unknown command: **${cmd}**. Type **/help** to see all available commands.`);
                    return;
            }

            // Send to AI backend
            sendAiMessage(aiMessage, cmdText);
        }

        // ── Render helpers ────────────────────────────────────────────────────────
        function appendMessage(role, text) {
            const div = document.createElement('div');
            div.className = `copilot-msg   copilot-msg--${role}`;

            const avatar = document.createElement('span');
            avatar.className = 'copilot-msg__avatar';
            avatar.setAttribute('aria-hidden', 'true');
            avatar.textContent = role === 'ai' ? '🤖' : '👤';

            const bubble = document.createElement('div');
            bubble.className = 'copilot-msg__bubble';
            // Render markdown-ish text (bold, lists, newlines) safely
            bubble.innerHTML = renderMarkdown(text);

            div.appendChild(avatar);
            div.appendChild(bubble);
            messagesEl.appendChild(div);
            messagesEl.scrollTop = messagesEl.scrollHeight;
            return div;
        }

        function appendErrorMessage(errorText, originalPayload) {
            const div = document.createElement('div');
            div.className = `copilot-msg copilot-msg--error`;

            const avatar = document.createElement('span');
            avatar.className = 'copilot-msg__avatar';
            avatar.setAttribute('aria-hidden', 'true');
            avatar.textContent = '❌';

            const bubble = document.createElement('div');
            bubble.className = 'copilot-msg__bubble';
            
            const errorTitle = document.createElement('strong');
            errorTitle.textContent = 'API Request Failed';
            
            const errorDesc = document.createElement('p');
            errorDesc.textContent = errorText;

            const retryBtn = document.createElement('button');
            retryBtn.className = 'copilot-retry-btn';
            retryBtn.textContent = '↻ Retry Request';
            retryBtn.onclick = () => {
                div.remove();
                sendAiMessage(originalPayload.text, originalPayload.displayMessage);
            };

            bubble.appendChild(errorTitle);
            bubble.appendChild(errorDesc);
            bubble.appendChild(retryBtn);

            div.appendChild(avatar);
            div.appendChild(bubble);
            messagesEl.appendChild(div);
            messagesEl.scrollTop = messagesEl.scrollHeight;
            return div;
        }

        function appendTypingIndicator() {
            const div = document.createElement('div');
            div.className = 'copilot-msg copilot-msg--ai copilot-msg--typing';
            div.innerHTML = `
                <span class="copilot-msg__avatar" aria-hidden="true">🤖</span>
                <div class="copilot-msg__bubble">
                    <span class="copilot-typing-dot"></span>
                    <span class="copilot-typing-dot"></span>
                    <span class="copilot-typing-dot"></span>
                </div>`;
            messagesEl.appendChild(div);
            messagesEl.scrollTop = messagesEl.scrollHeight;
            return div;
        }

        // ── Render actions ────────────────────────────────────────────────────────
        function renderActions(actions) {
            actionsEl.innerHTML = '';
            actions.forEach(action => {
                const btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'copilot-action-btn';
                btn.textContent = action.label;
                btn.dataset.actionType = action.actionType;
                btn.dataset.payload = JSON.stringify(action.payload ?? {});
                btn.setAttribute('aria-label', `Execute: ${action.label}`);
                btn.addEventListener('click', () => handleAction(action, btn));
                actionsEl.appendChild(btn);
            });
            actionsEl.style.display = 'flex';
        }

        async function handleAction(action, btn) {
            if (!confirm(`Execute: "${action.label}"?`)) return;
            btn.disabled = true;
            btn.textContent = 'Working...';

            try {
                if (action.actionType.startsWith('draft_')) {
                    launchWbsWizard(action);
                } else if (action.actionType === 'bulk-create') {
                    const resp = await fetch('/api/ai/bulk-create', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'RequestVerificationToken': getAntiForgeryToken()
                        },
                        body: action.payload
                    });
                    const result = await resp.json();
                    appendMessage('ai', `✅ Created ${result.createdIds?.length ?? 0} item(s) successfully.`);
                } else if (action.actionType === 'navigate') {
                    window.location.href = action.payload?.url ?? '/';
                } else {
                    appendMessage('ai', `Action "${action.label}" acknowledged.`);
                }
            } catch (err) {
                btn.disabled = false;
                btn.textContent = action.label;
                appendMessage('ai', `⚠ Failed to execute action: ${err.message}`);
            }

            actionsEl.style.display = 'none';
        }

        // ── WBS Wizard Integration ───────────────────────────────────────────────
        let draftWbsState = { projectId: null, epics: [] };

        function launchWbsWizard(action) {
            const payload = typeof action.payload === 'string' ? JSON.parse(action.payload) : action.payload;
            const args = typeof payload.args === 'string' ? JSON.parse(payload.args) : (payload.args ?? {});
            
            const root = document.getElementById('wbs-wizard-editor-root');
            if (!root) return;

            const myModal = new bootstrap.Modal(document.getElementById('wbsWizardModal'));
            myModal.show();
            
            if (action.actionType === 'draft_epics') {
                draftWbsState.projectId = args.projectId;
                draftWbsState.epics = args.epics || [];
                renderWbsWizardEpics(root);
            } else if (action.actionType === 'draft_features') {
                draftWbsState.epics = args.epics || [];
                renderWbsWizardFeatures(root);
            } else if (action.actionType === 'draft_stories_and_tasks') {
                draftWbsState.epics = args.epics || [];
                renderWbsWizardStories(root);
            }
        }

        function renderWbsWizardEpics(root) {
            root.innerHTML = `<h5 class="mb-3 text-primary"><i class="fas fa-layer-group"></i> Step 1: Review Epics</h5>`;
            const list = document.createElement('div');
            list.className = 'list-group mb-3';
            
            draftWbsState.epics.forEach((epic, idx) => {
                const item = document.createElement('div');
                item.className = 'list-group-item';
                item.innerHTML = `
                    <div class="mb-2"><strong><input type="text" class="form-control form-control-sm epic-name" data-idx="${idx}" value="${epic.name.replace(/"/g, '&quot;')}" /></strong></div>
                    <div><textarea class="form-control form-control-sm epic-desc" data-idx="${idx}" rows="2">${epic.description || ''}</textarea></div>
                `;
                list.appendChild(item);
            });
            root.appendChild(list);

            const applyBtn = document.getElementById('wbs-wizard-apply-btn');
            applyBtn.textContent = 'Next: Draft Features';
            applyBtn.onclick = () => {
                root.querySelectorAll('.epic-name').forEach(el => draftWbsState.epics[el.dataset.idx].name = el.value);
                root.querySelectorAll('.epic-desc').forEach(el => draftWbsState.epics[el.dataset.idx].description = el.value);
                
                const bootstrapModal = bootstrap.Modal.getInstance(document.getElementById('wbsWizardModal'));
                bootstrapModal.hide();
                
                sendAiMessage(`Here are the approved Epics. Please use draft_features to generate Features for them:\n\`\`\`json\n${JSON.stringify({ projectId: draftWbsState.projectId, epics: draftWbsState.epics }, null, 2)}\n\`\`\``);
            };
        }

        function renderWbsWizardFeatures(root) {
            root.innerHTML = `<h5 class="mb-3 text-primary"><i class="fas fa-cubes"></i> Step 2: Review Features</h5>`;
            const list = document.createElement('div');
            
            draftWbsState.epics.forEach((epic, eIdx) => {
                const epicCard = document.createElement('div');
                epicCard.className = 'card mb-3 shadow-sm border-0';
                epicCard.innerHTML = `<div class="card-header bg-white border-bottom-0 pt-3"><strong>Epic: ${epic.name}</strong></div>`;
                
                const featList = document.createElement('div');
                featList.className = 'list-group list-group-flush';
                (epic.features || []).forEach((feat, fIdx) => {
                    const item = document.createElement('div');
                    item.className = 'list-group-item bg-light';
                    item.innerHTML = `
                        <div class="mb-1"><input type="text" class="form-control form-control-sm feat-name" data-e="${eIdx}" data-f="${fIdx}" value="${feat.name.replace(/"/g, '&quot;')}" /></div>
                        <div><input type="text" class="form-control form-control-sm feat-desc" data-e="${eIdx}" data-f="${fIdx}" value="${feat.description || ''}" placeholder="Description" /></div>
                    `;
                    featList.appendChild(item);
                });
                epicCard.appendChild(featList);
                list.appendChild(epicCard);
            });
            root.appendChild(list);

            const applyBtn = document.getElementById('wbs-wizard-apply-btn');
            applyBtn.textContent = 'Next: Draft Stories & Tasks';
            applyBtn.onclick = () => {
                root.querySelectorAll('.feat-name').forEach(el => draftWbsState.epics[el.dataset.e].features[el.dataset.f].name = el.value);
                root.querySelectorAll('.feat-desc').forEach(el => draftWbsState.epics[el.dataset.e].features[el.dataset.f].description = el.value);
                
                const bootstrapModal = bootstrap.Modal.getInstance(document.getElementById('wbsWizardModal'));
                bootstrapModal.hide();
                
                sendAiMessage(`Here are the approved Features. Please use draft_stories_and_tasks to generate User Stories, Test Cases, and PERT-estimated Tasks for them:\n\`\`\`json\n${JSON.stringify({ projectId: draftWbsState.projectId, epics: draftWbsState.epics }, null, 2)}\n\`\`\``);
            };
        }

        function renderWbsWizardStories(root) {
            root.innerHTML = `<h5 class="mb-3 text-success"><i class="fas fa-check-circle"></i> Step 3: Final Review</h5>`;
            const pre = document.createElement('pre');
            pre.className = 'p-3 bg-dark text-white rounded small';
            pre.style.maxHeight = '400px';
            pre.textContent = JSON.stringify(draftWbsState.epics, null, 2);
            root.appendChild(pre);

            const applyBtn = document.getElementById('wbs-wizard-apply-btn');
            applyBtn.textContent = 'Commit to Database';
            applyBtn.onclick = async () => {
                applyBtn.disabled = true;
                applyBtn.textContent = 'Saving...';
                try {
                    const resp = await fetch('/api/wbs/bulk-create', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'RequestVerificationToken': getAntiForgeryToken()
                        },
                        body: JSON.stringify({ projectId: draftWbsState.projectId, wbs: draftWbsState.epics })
                    });
                    if (!resp.ok) throw new Error(await resp.text());
                    const result = await resp.text();
                    
                    const bootstrapModal = bootstrap.Modal.getInstance(document.getElementById('wbsWizardModal'));
                    bootstrapModal.hide();
                    
                    appendMessage('ai', `✅ **WBS Creation Complete:**\n${result}`);
                } catch (err) {
                    alert('Save failed: ' + err.message);
                } finally {
                    applyBtn.disabled = false;
                }
            };
        }

        // ── Simple markdown renderer (no external dependencies) ───────────────────
        function renderMarkdown(text) {
            if (!text) return '';

            // Escape HTML safely
            let html = text
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;');

            return parseMarkdownLineByLine(html);
        }

        function parseMarkdownLineByLine(text) {
            const lines = text.split('\n');
            let result = [];
            let inCodeBlock = false;
            let codeContent = [];
            let codeLang = '';
            
            let inTable = false;
            let tableRows = [];
            
            let listType = null; // 'ul', 'ol', 'checklist'
            let listItems = [];

            function flushList() {
                if (listType) {
                    if (listType === 'checklist') {
                        result.push(`<ul class="copilot-checklist-list">${listItems.join('')}</ul>`);
                    } else {
                        result.push(`<${listType}>${listItems.join('')}</${listType}>`);
                    }
                    listItems = [];
                    listType = null;
                }
            }

            function flushTable() {
                if (inTable) {
                    if (tableRows.length > 0) {
                        let html = '<div class="copilot-table-wrapper"><table class="table table-bordered table-sm copilot-table">';
                        tableRows.forEach((row, rowIndex) => {
                            if (rowIndex === 1 && row.every(cell => cell.trim().match(/^-+$/))) {
                                return; // skip divider
                            }
                            
                            const cellTag = rowIndex === 0 ? 'th' : 'td';
                            
                            if (rowIndex === 0) html += '<thead>';
                            else if (rowIndex === 1 && tableRows.length > 1) html += '<tbody>';
                            
                            html += '<tr>';
                            row.forEach(cell => {
                                html += `<${cellTag}>${cell.trim()}</${cellTag}>`;
                            });
                            html += '</tr>';
                            
                            if (rowIndex === 0) html += '</thead>';
                        });
                        if (tableRows.length > 1) html += '</tbody>';
                        html += '</table></div>';
                        result.push(html);
                    }
                    tableRows = [];
                    inTable = false;
                }
            }

            for (let i = 0; i < lines.length; i++) {
                let line = lines[i];

                if (line.trim().startsWith('```')) {
                    if (inCodeBlock) {
                        result.push(`<pre class="copilot-code-block"><code class="language-${codeLang}">${codeContent.join('\n')}</code></pre>`);
                        inCodeBlock = false;
                        codeContent = [];
                        codeLang = '';
                    } else {
                        flushList();
                        flushTable();
                        inCodeBlock = true;
                        codeLang = line.trim().substring(3).trim();
                    }
                    continue;
                }

                if (inCodeBlock) {
                    codeContent.push(line);
                    continue;
                }

                if (line.trim().startsWith('|') && line.trim().endsWith('|')) {
                    flushList();
                    inTable = true;
                    const cells = line.split('|').slice(1, -1);
                    tableRows.push(cells);
                    continue;
                } else {
                    flushTable();
                }

                const checklistMatch = line.match(/^-\s+\[( |x|X)\]\s+(.+)$/);
                if (checklistMatch) {
                    if (listType !== 'checklist') {
                        flushList();
                        listType = 'checklist';
                    }
                    const checked = checklistMatch[1].toLowerCase() === 'x';
                    const content = checklistMatch[2];
                    listItems.push(`<li class="copilot-checklist-item">
                        <input type="checkbox" disabled ${checked ? 'checked' : ''} class="copilot-checklist-checkbox">
                        <span>${content}</span>
                    </li>`);
                    continue;
                }

                const bulletMatch = line.match(/^[-*]\s+(.+)$/);
                if (bulletMatch && !line.trim().startsWith('|')) {
                    if (listType !== 'ul') {
                        flushList();
                        listType = 'ul';
                    }
                    listItems.push(`<li>${bulletMatch[1]}</li>`);
                    continue;
                }

                const numberedMatch = line.match(/^\d+\.\s+(.+)$/);
                if (numberedMatch) {
                    if (listType !== 'ol') {
                        flushList();
                        listType = 'ol';
                    }
                    listItems.push(`<li>${numberedMatch[1]}</li>`);
                    continue;
                }

                flushList();

                const headingMatch = line.match(/^(#{1,6})\s+(.+)$/);
                if (headingMatch) {
                    const level = headingMatch[1].length;
                    result.push(`<h${level} class="copilot-heading h${level + 2}">${headingMatch[2]}</h${level}>`);
                    continue;
                }

                if (line.trim() === '') {
                    result.push('<br>');
                } else {
                    result.push(`<p>${line}</p>`);
                }
            }

            flushList();
            flushTable();

            let output = result.join('\n');

            output = output.replace(/\*\*([\s\S]*?)\*\*/g, '<strong>$1</strong>');
            output = output.replace(/`([^`]+)`/g, '<code class="copilot-inline-code">$1</code>');
            output = output.replace(/(<br>){3,}/g, '<br><br>');

            // Regex-based clickable mention badges
            output = output.replace(/@(Project|Epic|Feature|UserStory|Task|Sprint|User):([\w-]+):([^<\s`|@*#\\]+(?:\s+[^<\s`|@*#\\]+)*)/g, (match, type, id, label) => {
                let controller = type;
                if (type.toLowerCase() === 'userstory') controller = 'UserStories';
                else if (type.toLowerCase() === 'user') return `<span class="copilot-mention-badge copilot-mention-badge--user">👤 ${label}</span>`;
                else controller = type + 's';

                return `<a href="/${controller}/Details/${id}" class="copilot-mention-badge copilot-mention-badge--${type.toLowerCase()}" title="Navigate to ${type} details">
                    <span class="badge-icon">${getBadgeIcon(type)}</span> ${label}
                </a>`;
            });

            return output;
        }

        function getBadgeIcon(type) {
            switch(type.toLowerCase()) {
                case 'project': return '📁';
                case 'epic': return '⚡';
                case 'feature': return '✨';
                case 'userstory': return '📖';
                case 'task': return '☑';
                case 'sprint': return '🏃';
                default: return '🏷';
            }
        }

        // ── Mentions & Commands Autocomplete Helpers ─────────────────────────────────
        function onInput() {
            mentions = mentions.filter(m => inputEl.value.includes(`@${m.type}:${m.label}`));
            updateBackdrop();

            const caretPos = inputEl.selectionStart;
            const textBeforeCaret = inputEl.value.slice(0, caretPos);
            const mentionMatch = textBeforeCaret.match(/@(\w*)$/);
            const commandMatch = textBeforeCaret.match(/\/(\w*)$/);

            if (mentionMatch) {
                activeTrigger = '@';
                const query = mentionMatch[1];
                clearTimeout(searchTimeout);
                searchTimeout = setTimeout(() => {
                    fetchMentions(query);
                }, 200);
            } else if (commandMatch && (caretPos === commandMatch[0].length || textBeforeCaret.charAt(caretPos - commandMatch[0].length - 1).match(/\s/))) {
                activeTrigger = '/';
                const query = commandMatch[1];
                renderCommandDropdown(query);
            } else {
                hideDropdown();
            }
        }

        function updateBackdrop() {
            let text = inputEl.value;
            text = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

            for (const m of mentions) {
                const chipText = `@${m.type}:${m.label}`;
                const escaped = chipText.replace(/[-\/\\^$*+?.()|[\]{}]/g, '\\$&');
                const regex = new RegExp(escaped, 'g');
                text = text.replace(regex, `<span class="copilot-mention-chip copilot-mention-chip--${m.type.toLowerCase()}">${chipText}</span>`);
            }

            backdropEl.innerHTML = text;
            autoResizeTextarea();
        }

        async function fetchMentions(query) {
            try {
                let url = `/api/agent/mention-search?q=${encodeURIComponent(query)}`;
                if (entityType === 'Project' && entityId) {
                    url += `&projectId=${entityId}`;
                } else {
                    const pageProjId = document.querySelector('meta[name="ai-project-id"]')?.content;
                    if (pageProjId) {
                        url += `&projectId=${pageProjId}`;
                    }
                }

                const resp = await fetch(url);
                if (!resp.ok) return;

                const items = await resp.json();
                renderDropdown(items);
            } catch (err) {
                console.error("Mention search error:", err);
            }
        }

        const commands = [
            { name: '/help',       desc: 'Display all available commands and copilot capabilities', icon: '❓' },
            { name: '/plan',       desc: 'Paste meeting notes or description → AI generates full project WBS', icon: '🗺' },
            { name: '/report',     desc: 'Generate a PMP-grade status report for the active project', icon: '📋' },
            { name: '/risk',       desc: 'Analyze and list all risks for the active project', icon: '⚠' },
            { name: '/sprint',     desc: 'AI-driven smart sprint planning — assign tasks by capacity', icon: '🚀' },
            { name: '/standup',    desc: 'Generate today\'s daily standup digest for your tasks', icon: '🌅' },
            { name: '/testcases',  desc: 'Generate BDD test cases for a user story', icon: '🧪' },
            { name: '/estimate',   desc: 'Get a PERT three-point estimate for any task description', icon: '📊' },
            { name: '/read',       desc: 'Read and summarize tasks, sprints, or project status', icon: '🔍' },
            { name: '/capacity',   desc: 'Navigate to the capacity planning view', icon: '🏃' },
            { name: '/pert',       desc: 'Show the PERT estimation formula', icon: '📐' },
            { name: '/reestimate', desc: 'Guidelines for bulk AI re-estimation', icon: '🔄' },
        ];

        function renderCommandDropdown(query) {
            const filtered = commands.filter(c => c.name.toLowerCase().includes('/' + query.toLowerCase()));
            dropdownItems = filtered.map(c => ({
                type: 'Command',
                id: c.name,
                label: c.name,
                hint: c.desc,
                icon: c.icon
            }));

            if (dropdownItems.length === 0) {
                hideDropdown();
                return;
            }

            dropdownEl.innerHTML = '';
            activeDropdownIndex = 0;
            dropdownVisible = true;
            dropdownEl.style.display = 'block';

            dropdownItems.forEach((item, index) => {
                const div = document.createElement('div');
                div.className = 'copilot-mention-item' + (index === 0 ? ' copilot-mention-item--active' : '');
                
                div.innerHTML = `
                    <span class="copilot-mention-item__icon">${item.icon}</span>
                    <span class="copilot-mention-item__label">${escapeHtml(item.label)}</span>
                    <span class="copilot-mention-item__hint">${escapeHtml(item.hint)}</span>
                `;

                div.addEventListener('click', () => {
                    activeDropdownIndex = index;
                    selectActiveDropdownItem();
                });

                dropdownEl.appendChild(div);
            });
        }

        function renderDropdown(items) {
            dropdownItems = items;
            if (items.length === 0) {
                hideDropdown();
                return;
            }

            dropdownEl.innerHTML = '';
            activeDropdownIndex = 0;
            dropdownVisible = true;
            dropdownEl.style.display = 'block';

            items.forEach((item, index) => {
                const div = document.createElement('div');
                div.className = 'copilot-mention-item' + (index === 0 ? ' copilot-mention-item--active' : '');
                
                let icon = '🏷';
                if (item.type === 'Project') icon = '📁';
                else if (item.type === 'Epic') icon = '⚡';
                else if (item.type === 'Feature') icon = '✨';
                else if (item.type === 'UserStory') icon = '📖';
                else if (item.type === 'Task') icon = '☑';
                else if (item.type === 'Sprint') icon = '🏃';
                else if (item.type === 'User') icon = '👤';

                div.innerHTML = `
                    <span class="copilot-mention-item__icon">${icon}</span>
                    <span class="copilot-mention-item__label">${escapeHtml(item.label)}</span>
                    <span class="copilot-mention-item__hint">${escapeHtml(item.hint)}</span>
                `;

                div.addEventListener('click', () => {
                    activeDropdownIndex = index;
                    selectActiveDropdownItem();
                });

                dropdownEl.appendChild(div);
            });
        }

        function navigateDropdown(dir) {
            if (dropdownItems.length === 0) return;
            const items = dropdownEl.querySelectorAll('.copilot-mention-item');
            items[activeDropdownIndex].classList.remove('copilot-mention-item--active');

            activeDropdownIndex = (activeDropdownIndex + dir + dropdownItems.length) % dropdownItems.length;

            items[activeDropdownIndex].classList.add('copilot-mention-item--active');
            items[activeDropdownIndex].scrollIntoView({ block: 'nearest' });
        }

        function selectActiveDropdownItem() {
            if (activeDropdownIndex < 0 || activeDropdownIndex >= dropdownItems.length) return;
            const item = dropdownItems[activeDropdownIndex];

            const caretPos = inputEl.selectionStart;
            const textBeforeCaret = inputEl.value.slice(0, caretPos);

            if (activeTrigger === '@') {
                const match = textBeforeCaret.match(/@(\w*)$/);
                if (match) {
                    const startIndex = caretPos - match[0].length;
                    const beforeText = inputEl.value.slice(0, startIndex);
                    const afterText = inputEl.value.slice(caretPos);
                    const insertText = `@${item.type}:${item.label} `;

                    inputEl.value = beforeText + insertText + afterText;
                    
                    const newCaretPos = startIndex + insertText.length;
                    inputEl.setSelectionRange(newCaretPos, newCaretPos);

                    mentions.push({
                        type: item.type,
                        id: item.id,
                        label: item.label
                    });
                }
            } else if (activeTrigger === '/') {
                const match = textBeforeCaret.match(/\/(\w*)$/);
                if (match) {
                    const startIndex = caretPos - match[0].length;
                    const beforeText = inputEl.value.slice(0, startIndex);
                    const afterText = inputEl.value.slice(caretPos);
                    const insertText = `${item.label} `;

                    inputEl.value = beforeText + insertText + afterText;
                    
                    const newCaretPos = startIndex + insertText.length;
                    inputEl.setSelectionRange(newCaretPos, newCaretPos);
                }
            }

            hideDropdown();
            inputEl.focus();
            updateBackdrop();
        }

        function hideDropdown() {
            dropdownVisible = false;
            dropdownEl.style.display = 'none';
            dropdownItems = [];
            activeDropdownIndex = -1;
            activeTrigger = null;
        }

        function escapeHtml(str) {
            if (!str) return '';
            return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }

        // ── Utilities ─────────────────────────────────────────────────────────────
        function getAntiForgeryToken() {
            return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
        }

        // ── WBS Wizard State & Operations ─────────────────────────────────────────
        let currentWbsState = null;
        let activeProjectId = 1;

        function tryParseWbs(text) {
            if (!text) return null;
            const startIdx = text.indexOf('{');
            const endIdx = text.lastIndexOf('}');
            if (startIdx === -1 || endIdx === -1 || startIdx >= endIdx) return null;
            
            const jsonCandidate = text.substring(startIdx, endIdx + 1);
            try {
                const obj = JSON.parse(jsonCandidate);
                if (obj.name === 'bulk_create_wbs' && obj.arguments) {
                    return obj.arguments;
                }
                if (obj.wbs && Array.isArray(obj.wbs)) {
                    return obj;
                }
            } catch (e) {
                // Not WBS JSON
            }
            return null;
        }

        function renderWbsProposedCard(wbsData) {
            const firstEpic = wbsData.wbs && wbsData.wbs[0];
            const epicName = firstEpic ? (firstEpic.name || firstEpic.description || "Project Plan") : "Malpractice Reduction Initiative";
            let epicCount = wbsData.wbs ? wbsData.wbs.length : 0;
            let featureCount = 0;
            let storyCount = 0;
            let taskCount = 0;
            let totalEst = 0;

            if (wbsData.wbs) {
                wbsData.wbs.forEach(e => {
                    if (e.features) {
                        featureCount += e.features.length;
                        e.features.forEach(f => {
                            if (f.stories) {
                                storyCount += f.stories.length;
                                f.stories.forEach(s => {
                                    if (s.tasks) {
                                        taskCount += s.tasks.length;
                                        s.tasks.forEach(t => {
                                            const o = parseFloat(t.optimisticHours || t.optimistic) || 0;
                                            const m = parseFloat(t.mostLikelyHours || t.mostLikely) || 0;
                                            const p = parseFloat(t.pessimisticHours || t.pessimistic) || 0;
                                            const pert = (o > 0 && m > 0 && p > 0) ? (o + 4*m + p) / 6 : (m || 0);
                                            totalEst += pert;
                                        });
                                    }
                                });
                            }
                        });
                    }
                });
            }

            const wbsDataId = "wbs-data-" + Date.now();
            window[wbsDataId] = wbsData;

            return `
                <div class="copilot-wbs-card">
                    <div class="copilot-wbs-card__header">
                        <span>🗺</span> AI Proposed Project Plan
                    </div>
                    <div class="copilot-wbs-card__title">${escapeHtml(epicName)}</div>
                    <div class="copilot-wbs-card__summary">
                        <strong>Project ID:</strong> ${wbsData.projectId || 1}<br>
                        <strong>Structure:</strong> ${epicCount} Epic(s), ${featureCount} Feature(s), ${storyCount} User Story(ies), ${taskCount} Task(s)<br>
                        <strong>Est. Total Effort:</strong> ${totalEst.toFixed(1)} hours (PERT calculated)
                    </div>
                    <div class="copilot-wbs-card__actions">
                        <button class="copilot-wbs-card-btn copilot-wbs-card-btn--primary" onclick="triggerWbsWizard('${wbsDataId}')">
                            Modify & Apply Plan
                        </button>
                        <button class="copilot-wbs-card-btn" style="border: 1px solid #cbd5e1; background: #fff; color: #475569;" onclick="this.closest('.copilot-wbs-card').remove()">
                            Discard
                        </button>
                    </div>
                </div>
            `;
        }

        window.triggerWbsWizard = function(wbsDataId) {
            const wbsData = window[wbsDataId];
            if (wbsData) {
                openWbsWizard(wbsData);
            }
        };

        function openWbsWizard(wbsData) {
            currentWbsState = JSON.parse(JSON.stringify(wbsData.wbs || []));
            activeProjectId = parseInt(wbsData.projectId) || getActiveProjectContextId() || 1;
            
            renderWbsEditor();

            const modalEl = document.getElementById('wbsWizardModal');
            const modal = new bootstrap.Modal(modalEl);
            modal.show();
        }

        function escapeHtmlAttr(str) {
            if (!str) return '';
            return str
                .replace(/&/g, '&amp;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;');
        }

        function renderWbsEditor() {
            const container = document.getElementById('wbs-wizard-editor-root');
            container.innerHTML = '';

            if (!currentWbsState || currentWbsState.length === 0) {
                container.innerHTML = `<div class="text-center p-5 text-muted">No epics in this plan. Click "Add Epic" to start.</div>`;
                return;
            }

            currentWbsState.forEach((epic, epicIdx) => {
                const epicDiv = document.createElement('div');
                epicDiv.className = 'wbs-epic-block';
                epicDiv.innerHTML = `
                    <div class="wbs-block-header">
                        <span class="wbs-block-badge wbs-epic-badge">Epic</span>
                        <button type="button" class="wbs-btn-delete" title="Delete Epic">✕ Remove Epic</button>
                    </div>
                    <div class="row g-2">
                        <div class="col-md-6 wbs-input-group">
                            <label>Epic Name</label>
                            <input type="text" class="wbs-input wbs-epic-name" value="${escapeHtmlAttr(epic.name || epic.description || '')}">
                        </div>
                        <div class="col-md-6 wbs-input-group">
                            <label>Description</label>
                            <input type="text" class="wbs-input wbs-epic-desc" value="${escapeHtmlAttr(epic.description || epic.name || '')}">
                        </div>
                    </div>
                    <div class="wbs-features-container"></div>
                    <button type="button" class="wbs-add-btn wbs-add-feature-btn">➕ Add Feature</button>
                `;

                const nameInput = epicDiv.querySelector('.wbs-epic-name');
                nameInput.addEventListener('input', (e) => { epic.name = e.target.value; });
                const descInput = epicDiv.querySelector('.wbs-epic-desc');
                descInput.addEventListener('input', (e) => { epic.description = e.target.value; });

                epicDiv.querySelector('.wbs-block-header .wbs-btn-delete').addEventListener('click', () => {
                    currentWbsState.splice(epicIdx, 1);
                    renderWbsEditor();
                });

                epicDiv.querySelector('.wbs-add-feature-btn').addEventListener('click', () => {
                    if (!epic.features) epic.features = [];
                    epic.features.push({
                        name: 'New Feature',
                        description: '',
                        stories: []
                    });
                    renderWbsEditor();
                });

                const featuresContainer = epicDiv.querySelector('.wbs-features-container');
                if (epic.features) {
                    epic.features.forEach((feature, featIdx) => {
                        const featDiv = document.createElement('div');
                        featDiv.className = 'wbs-feature-block';
                        featDiv.innerHTML = `
                            <div class="wbs-block-header">
                                <span class="wbs-block-badge wbs-feature-badge">Feature</span>
                                <button type="button" class="wbs-btn-delete" title="Delete Feature">✕ Remove Feature</button>
                            </div>
                            <div class="row g-2">
                                <div class="col-md-6 wbs-input-group">
                                    <label>Feature Name</label>
                                    <input type="text" class="wbs-input wbs-feat-name" value="${escapeHtmlAttr(feature.name || '')}">
                                </div>
                                <div class="col-md-6 wbs-input-group">
                                    <label>Description</label>
                                    <input type="text" class="wbs-input wbs-feat-desc" value="${escapeHtmlAttr(feature.description || '')}">
                                </div>
                            </div>
                            <div class="wbs-stories-container"></div>
                            <button type="button" class="wbs-add-btn wbs-add-story-btn">➕ Add User Story</button>
                        `;

                        featDiv.querySelector('.wbs-feat-name').addEventListener('input', (e) => { feature.name = e.target.value; });
                        featDiv.querySelector('.wbs-feat-desc').addEventListener('input', (e) => { feature.description = e.target.value; });

                        featDiv.querySelector('.wbs-block-header .wbs-btn-delete').addEventListener('click', () => {
                            epic.features.splice(featIdx, 1);
                            renderWbsEditor();
                        });

                        featDiv.querySelector('.wbs-add-story-btn').addEventListener('click', () => {
                            if (!feature.stories) feature.stories = [];
                            feature.stories.push({
                                title: 'New User Story',
                                description: '',
                                acceptanceCriteria: '',
                                tasks: []
                            });
                            renderWbsEditor();
                        });

                        const storiesContainer = featDiv.querySelector('.wbs-stories-container');
                        if (feature.stories) {
                            feature.stories.forEach((story, storyIdx) => {
                                const storyDiv = document.createElement('div');
                                storyDiv.className = 'wbs-story-block';
                                storyDiv.innerHTML = `
                                    <div class="wbs-block-header">
                                        <span class="wbs-block-badge wbs-story-badge">User Story</span>
                                        <button type="button" class="wbs-btn-delete" title="Delete Story">✕ Remove Story</button>
                                    </div>
                                    <div class="row g-2">
                                        <div class="col-md-4 wbs-input-group">
                                            <label>Story Title</label>
                                            <input type="text" class="wbs-input wbs-story-title" value="${escapeHtmlAttr(story.title || '')}">
                                        </div>
                                        <div class="col-md-4 wbs-input-group">
                                            <label>Description</label>
                                            <input type="text" class="wbs-input wbs-story-desc" value="${escapeHtmlAttr(story.description || '')}">
                                        </div>
                                        <div class="col-md-4 wbs-input-group">
                                            <label>Acceptance Criteria</label>
                                            <input type="text" class="wbs-input wbs-story-ac" value="${escapeHtmlAttr(story.acceptanceCriteria || '')}">
                                        </div>
                                    </div>
                                    <div class="wbs-tasks-container"></div>
                                    <button type="button" class="wbs-add-btn wbs-add-task-btn">➕ Add Task</button>
                                `;

                                storyDiv.querySelector('.wbs-story-title').addEventListener('input', (e) => { story.title = e.target.value; });
                                storyDiv.querySelector('.wbs-story-desc').addEventListener('input', (e) => { story.description = e.target.value; });
                                storyDiv.querySelector('.wbs-story-ac').addEventListener('input', (e) => { story.acceptanceCriteria = e.target.value; });

                                storyDiv.querySelector('.wbs-block-header .wbs-btn-delete').addEventListener('click', () => {
                                    feature.stories.splice(storyIdx, 1);
                                    renderWbsEditor();
                                });

                                storyDiv.querySelector('.wbs-add-task-btn').addEventListener('click', () => {
                                    if (!story.tasks) story.tasks = [];
                                    story.tasks.push({
                                        title: 'New Task',
                                        description: '',
                                        optimisticHours: 4,
                                        mostLikelyHours: 8,
                                        pessimisticHours: 16
                                    });
                                    renderWbsEditor();
                                });

                                const tasksContainer = storyDiv.querySelector('.wbs-tasks-container');
                                if (story.tasks) {
                                    story.tasks.forEach((task, taskIdx) => {
                                        const taskDiv = document.createElement('div');
                                        taskDiv.className = 'wbs-task-block';

                                        const optVal = task.optimisticHours !== undefined ? task.optimisticHours : (task.optimistic !== undefined ? task.optimistic : 0);
                                        const mlVal = task.mostLikelyHours !== undefined ? task.mostLikelyHours : (task.mostLikely !== undefined ? task.mostLikely : 0);
                                        const pesVal = task.pessimisticHours !== undefined ? task.pessimisticHours : (task.pessimistic !== undefined ? task.pessimistic : 0);
                                        const pertVal = (optVal > 0 && mlVal > 0 && pesVal > 0) ? (parseFloat(optVal) + 4 * parseFloat(mlVal) + parseFloat(pesVal)) / 6 : 0;

                                        taskDiv.innerHTML = `
                                            <div class="wbs-block-header">
                                                <span class="wbs-block-badge wbs-task-badge">Task</span>
                                                <button type="button" class="wbs-btn-delete" title="Delete Task">✕ Remove Task</button>
                                            </div>
                                            <div class="row g-2">
                                                <div class="col-md-6 wbs-input-group">
                                                    <label>Task Title</label>
                                                    <input type="text" class="wbs-input wbs-task-title" value="${escapeHtmlAttr(task.title || '')}">
                                                </div>
                                                <div class="col-md-6 wbs-input-group">
                                                    <label>Description</label>
                                                    <input type="text" class="wbs-input wbs-task-desc" value="${escapeHtmlAttr(task.description || '')}">
                                                </div>
                                            </div>
                                            <div class="wbs-task-estimates-grid">
                                                <div class="wbs-input-group">
                                                    <label>Optimistic (O)</label>
                                                    <input type="number" step="0.5" min="0" class="wbs-input wbs-task-opt" value="${optVal}">
                                                </div>
                                                <div class="wbs-input-group">
                                                    <label>Most Likely (M)</label>
                                                    <input type="number" step="0.5" min="0" class="wbs-input wbs-task-ml" value="${mlVal}">
                                                </div>
                                                <div class="wbs-input-group">
                                                    <label>Pessimistic (P)</label>
                                                    <input type="number" step="0.5" min="0" class="wbs-input wbs-task-pes" value="${pesVal}">
                                                </div>
                                                <div class="wbs-pert-display">
                                                    <label style="font-size:0.7rem; color:#0369a1; font-weight:600; margin-bottom:0.1rem;">PERT</label>
                                                    <div class="wbs-pert-val">${pertVal.toFixed(1)}h</div>
                                                </div>
                                            </div>
                                        `;

                                        taskDiv.querySelector('.wbs-task-title').addEventListener('input', (e) => { task.title = e.target.value; });
                                        taskDiv.querySelector('.wbs-task-desc').addEventListener('input', (e) => { task.description = e.target.value; });

                                        const optInput = taskDiv.querySelector('.wbs-task-opt');
                                        const mlInput = taskDiv.querySelector('.wbs-task-ml');
                                        const pesInput = taskDiv.querySelector('.wbs-task-pes');
                                        const pertDisplay = taskDiv.querySelector('.wbs-pert-val');

                                        const updatePert = () => {
                                            const o = parseFloat(optInput.value) || 0;
                                            const m = parseFloat(mlInput.value) || 0;
                                            const p = parseFloat(pesInput.value) || 0;
                                            
                                            task.optimisticHours = o;
                                            task.mostLikelyHours = m;
                                            task.pessimisticHours = p;
                                            task.optimistic = o;
                                            task.mostLikely = m;
                                            task.pessimistic = p;

                                            const calculated = (o > 0 && m > 0 && p > 0) ? (o + 4 * m + p) / 6 : 0;
                                            pertDisplay.textContent = calculated.toFixed(1) + 'h';
                                        };

                                        optInput.addEventListener('input', updatePert);
                                        mlInput.addEventListener('input', updatePert);
                                        pesInput.addEventListener('input', updatePert);

                                        taskDiv.querySelector('.wbs-block-header .wbs-btn-delete').addEventListener('click', () => {
                                            story.tasks.splice(taskIdx, 1);
                                            renderWbsEditor();
                                        });

                                        tasksContainer.appendChild(taskDiv);
                                    });
                                }

                                storiesContainer.appendChild(storyDiv);
                            });
                        }

                        featuresContainer.appendChild(featDiv);
                    });
                }

                container.appendChild(epicDiv);
            });
        }

        // Add Epic click handler
        const addEpicBtn = document.getElementById('wbs-wizard-add-epic-btn');
        if (addEpicBtn) {
            addEpicBtn.addEventListener('click', () => {
                if (!currentWbsState) currentWbsState = [];
                currentWbsState.push({
                    name: 'New Epic',
                    description: '',
                    features: []
                });
                renderWbsEditor();
            });
        }

        // Apply WBS click handler
        const applyBtn = document.getElementById('wbs-wizard-apply-btn');
        if (applyBtn) {
            applyBtn.addEventListener('click', async () => {
                if (!currentWbsState || currentWbsState.length === 0) {
                    alert('No epics to save.');
                    return;
                }

                applyBtn.disabled = true;
                applyBtn.textContent = 'Saving Plan...';

                try {
                    const payload = {
                        projectId: activeProjectId,
                        wbs: currentWbsState
                    };

                    const resp = await fetch('/api/ai/create-wbs', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'RequestVerificationToken': getAntiForgeryToken()
                        },
                        body: JSON.stringify(payload)
                    });

                    if (!resp.ok) {
                        const errMsg = await resp.text();
                        throw new Error(errMsg || 'Failed to save WBS.');
                    }

                    const result = await resp.json();
                    
                    const modalEl = document.getElementById('wbsWizardModal');
                    const modal = bootstrap.Modal.getInstance(modalEl);
                    if (modal) modal.hide();

                    appendMessage('ai', `✅ **WBS Plan successfully applied!**\n\n${result.message}`);
                    
                    if (confirm('WBS plan created successfully! Would you like to reload the page to see the new items?')) {
                        window.location.reload();
                    }
                } catch (err) {
                    alert('Error saving plan: ' + err.message);
                } finally {
                    applyBtn.disabled = false;
                    applyBtn.textContent = 'Apply & Create Plan';
                }
            });
        }

        // ── Session Bar Operations ────────────────────────────────────────────────
        async function refreshSessions(autoSelectSessionId, loadHistory = false) {
            if (!sessionSelector || !sessionBar) return;
            const projectId = getActiveProjectContextId();
            if (!projectId) {
                sessionBar.style.display = 'none';
                return;
            }

            try {
                const resp = await fetch(`/api/agent/project-sessions/${projectId}`);
                if (resp.ok) {
                    const sessions = await resp.json();
                    sessionSelector.innerHTML = '';

                    sessions.forEach(s => {
                        const opt = document.createElement('option');
                        opt.value = s.id;
                        opt.textContent = s.title;
                        sessionSelector.appendChild(opt);
                    });

                    sessionBar.style.display = 'flex';

                    if (autoSelectSessionId) {
                        const exists = sessions.some(s => s.id === autoSelectSessionId);
                        if (exists) {
                            sessionSelector.value = autoSelectSessionId;
                            conversationId = autoSelectSessionId;
                            sessionStorage.setItem('copilot-conv-id', conversationId);
                            if (loadHistory) {
                                await switchSession(autoSelectSessionId);
                            }
                        } else {
                            // If it's a new unsaved session, prepend a temporary option for it
                            const opt = document.createElement('option');
                            opt.value = autoSelectSessionId;
                            opt.textContent = '-- New Session --';
                            sessionSelector.insertBefore(opt, sessionSelector.firstChild);
                            sessionSelector.value = autoSelectSessionId;
                            conversationId = autoSelectSessionId;
                            sessionStorage.setItem('copilot-conv-id', conversationId);
                            if (loadHistory) {
                                resetChatUI();
                            }
                        }
                    } else if (sessions.length > 0) {
                        sessionSelector.value = sessions[0].id;
                        await switchSession(sessions[0].id);
                    } else {
                        startNewSession();
                    }
                }
            } catch (err) {
                console.error('Failed to load project sessions', err);
            }
        }

        async function switchSession(sessId) {
            conversationId = sessId;
            sessionStorage.setItem('copilot-conv-id', conversationId);
            
            resetChatUI();

            try {
                const resp = await fetch(`/api/agent/conversation-history/${conversationId}`);
                if (resp.ok) {
                    const turns = await resp.json();
                    turns.forEach(turn => {
                        const role = turn.role === 'model' ? 'ai' : 'user';
                        if (role === 'ai') {
                            const wbsData = tryParseWbs(turn.text);
                            if (wbsData) {
                                const aiMsgDiv = appendMessage('ai', '');
                                const bubble = aiMsgDiv.querySelector('.copilot-msg__bubble');
                                bubble.innerHTML = renderWbsProposedCard(wbsData);
                            } else {
                                appendMessage('ai', turn.displayText || turn.text);
                            }
                        } else {
                            appendMessage('user', turn.displayText || turn.text);
                        }
                    });
                }
            } catch (err) {
                console.error('Failed to load conversation history', err);
            }
        }

        function startNewSession() {
            conversationId = generateConvId();
            sessionStorage.setItem('copilot-conv-id', conversationId);
            resetChatUI();

            if (sessionSelector) {
                sessionSelector.innerHTML = '<option value="' + conversationId + '">-- New Session --</option>';
                sessionSelector.value = conversationId;
            }
        }

        function resetChatUI() {
            const msgs = messagesEl.querySelectorAll('.copilot-msg');
            msgs.forEach((m, i) => { if (i > 0) m.remove(); });
            actionsEl.style.display = 'none';
            actionsEl.innerHTML = '';
            mentions = [];
            updateBackdrop();
        }

        // ── Failed Jobs Management ────────────────────────────────────────────────
        if (failedJobsToggle) {
            failedJobsToggle.addEventListener('click', () => {
                if (failedJobsList.style.display === 'none') {
                    failedJobsList.style.display = 'flex';
                    failedJobsToggle.textContent = 'Hide';
                } else {
                    failedJobsList.style.display = 'none';
                    failedJobsToggle.textContent = 'Show';
                }
            });
        }

        async function loadFailedJobs() {
            window.loadFailedJobs = loadFailedJobs;
            if (!failedJobsPanel) return;
            const projectId = getActiveProjectContextId();
            const url = `/api/agent/failed-jobs` + (projectId ? `?projectId=${projectId}` : '');

            try {
                const resp = await fetch(url, {
                    method: 'GET',
                    headers: { 'Accept': 'application/json' }
                });
                if (!resp.ok) return;
                const jobs = await resp.json();

                if (jobs.length > 0) {
                    failedJobsCount.textContent = jobs.length;
                    failedJobsPanel.style.display = 'block';

                    failedJobsList.innerHTML = jobs.map(job => {
                        let desc = job.jobType;
                        try {
                            const payload = JSON.parse(job.requestPayloadJson);
                            if (payload.title) desc += `: "${payload.title}"`;
                            else if (payload.message) desc += `: "${payload.message}"`;
                        } catch(e) {}
                        
                        const dateStr = new Date(job.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

                        return `
                            <div class="failed-job-card" data-id="${job.id}" style="background: rgba(255,255,255,0.03); border: 1px solid rgba(239, 68, 68, 0.2); border-radius: 4px; padding: 0.4rem; display: flex; flex-direction: column; gap: 0.2rem; font-size: 0.72rem;">
                                <div style="display: flex; align-items: center; justify-content: space-between; font-weight: 600; color: #fca5a5;">
                                    <span>${escapeHtml(job.jobType)}</span>
                                    <span style="font-weight: normal; font-size: 0.65rem; color: #94a3b8;">${dateStr}</span>
                                </div>
                                <div style="color: #cbd5e1; word-break: break-all; max-height: 32px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                                    ${escapeHtml(desc)}
                                </div>
                                <div style="color: #ef4444; font-size: 0.65rem; font-style: italic;">
                                    ${escapeHtml(job.errorMessage || 'Unknown error')}
                                </div>
                                <div style="display: flex; gap: 0.3rem; margin-top: 0.2rem; align-self: flex-end;">
                                    <button type="button" class="btn-resume-job" data-id="${job.id}" style="background: rgba(99,102,241,0.2); border: 1px solid #6366f1; color: #818cf8; border-radius: 3px; padding: 0.15rem 0.4rem; font-size: 0.65rem; cursor: pointer; font-weight: 500;">Resume</button>
                                    <button type="button" class="btn-dismiss-job" data-id="${job.id}" style="background: rgba(239, 68, 68, 0.1); border: 1px solid rgba(239, 68, 68, 0.3); color: #f87171; border-radius: 3px; padding: 0.15rem 0.4rem; font-size: 0.65rem; cursor: pointer;">Dismiss</button>
                                </div>
                            </div>
                        `;
                    }).join('');

                    // Wire events on buttons
                    failedJobsList.querySelectorAll('.btn-resume-job').forEach(btn => {
                        btn.addEventListener('click', () => resumeJob(btn.dataset.id, btn));
                    });
                    failedJobsList.querySelectorAll('.btn-dismiss-job').forEach(btn => {
                        btn.addEventListener('click', () => dismissJob(btn.dataset.id));
                    });
                } else {
                    failedJobsPanel.style.display = 'none';
                    failedJobsList.style.display = 'none';
                    if (failedJobsToggle) failedJobsToggle.textContent = 'Show';
                }
            } catch (err) {
                console.error("Failed to load failed background jobs", err);
            }
        }

        async function resumeJob(jobId, btn) {
            btn.disabled = true;
            btn.textContent = 'Retrying...';
            
            const activeSessionId = conversationId; 
            const url = `/api/agent/failed-jobs/${jobId}/resume?conversationId=${activeSessionId || ''}`;

            try {
                const resp = await fetch(url, {
                    method: 'POST',
                    headers: {
                        'RequestVerificationToken': getAntiForgeryToken()
                    }
                });

                if (resp.ok) {
                    await loadFailedJobs();
                    if (activeSessionId) {
                        await switchSession(activeSessionId);
                    }
                } else {
                    const errMsg = await resp.text();
                    alert(`Resume failed: ${errMsg}`);
                    btn.disabled = false;
                    btn.textContent = 'Resume';
                }
            } catch (err) {
                alert(`Network error during resume: ${err.message}`);
                btn.disabled = false;
                btn.textContent = 'Resume';
            }
        }

        async function dismissJob(jobId) {
            try {
                const resp = await fetch(`/api/agent/failed-jobs/${jobId}`, {
                    method: 'DELETE',
                    headers: {
                        'RequestVerificationToken': getAntiForgeryToken()
                    }
                });
                if (resp.ok) {
                    await loadFailedJobs();
                } else {
                    alert('Failed to dismiss job.');
                }
            } catch (err) {
                alert(`Network error during dismiss: ${err.message}`);
            }
        }
    });

    function generateConvId() {
        return 'conv-' + Math.random().toString(36).slice(2, 11);
    }
})();
