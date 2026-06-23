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

        // Keep backdrop scroll in sync with textarea scroll
        inputEl.addEventListener('scroll', () => {
            backdropEl.scrollTop = inputEl.scrollTop;
            backdropEl.scrollLeft = inputEl.scrollLeft;
        });

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

            conversationId = generateConvId();
            sessionStorage.setItem('copilot-conv-id', conversationId);

            // Remove all messages except the welcome message (first child)
            const msgs = messagesEl.querySelectorAll('.copilot-msg');
            msgs.forEach((m, i) => { if (i > 0) m.remove(); });
            actionsEl.style.display = 'none';
            actionsEl.innerHTML = '';

            mentions = [];
            updateBackdrop();
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

        async function sendAiMessage(text) {
            isSending = true;
            sendBtn.disabled = true;
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
                        projectContextId: getActiveProjectContextId()
                    })
                });

                typingEl.remove();
                mentions = [];
                updateBackdrop();

                if (!streamResp.ok) {
                    const err = await streamResp.text();
                    appendMessage('ai', `⚠ Error: ${err || 'AI service unavailable.'}`);
                    return;
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
                        try {
                            const obj = JSON.parse(line);
                            if (obj.done) break;
                            if (obj.chunk) {
                                fullText += obj.chunk;
                                bubble.innerHTML = renderMarkdown(fullText);
                                messagesEl.scrollTop = messagesEl.scrollHeight;
                            }
                            if (obj.actions) actions = obj.actions;
                        } catch { /* skip malformed chunk */ }
                    }
                }

                // Final render
                bubble.innerHTML = renderMarkdown(fullText || '(no response)');
                
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

                if (actions?.length) {
                    renderActions(actions);
                } else {
                    actionsEl.style.display = 'none';
                }
            } catch (err) {
                typingEl?.remove();
                appendMessage('ai', `⚠ Network error: ${err.message}`);
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
                        ? `Parse these meeting notes and create a full project Work Breakdown Structure (Epics → Features → User Stories → Tasks). Use the bulk_create_wbs tool to create everything in the database. Meeting notes:\n\n${args}`
                        : 'I want to create a project plan from meeting notes. Please ask me to paste my meeting notes and then generate a full WBS with Epics, Features, User Stories, and Tasks.';
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
            sendAiMessage(aiMessage);
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
                if (action.actionType === 'bulk-create') {
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
    });

    function generateConvId() {
        return 'conv-' + Math.random().toString(36).slice(2, 11);
    }
})();
