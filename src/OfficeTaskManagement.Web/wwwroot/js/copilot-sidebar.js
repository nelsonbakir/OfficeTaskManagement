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
        const inputWrapper = document.querySelector('.copilot-sidebar__input-wrapper');
        
        const backdropEl = document.createElement('div');
        backdropEl.className = 'copilot-sidebar__input-backdrop';
        inputWrapper.insertBefore(backdropEl, inputEl);

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

        // ── Send message ──────────────────────────────────────────────────────────
        async function sendMessage() {
            const text = inputEl.value.trim();
            if (!text || isSending) return;

            if (text.startsWith('/')) {
                handleSlashCommand(text);
                inputEl.value = '';
                charCountEl.textContent = '0/2000';
                return;
            }

            isSending = true;
            sendBtn.disabled = true;
            appendMessage('user', text);
            inputEl.value = '';
            charCountEl.textContent = '0/2000';

            const typingEl = appendTypingIndicator();

            try {
                const resp = await fetch('/api/agent/chat', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': getAntiForgeryToken()
                    },
                    body: JSON.stringify({
                        conversationId,
                        userId: '',       // server fills from ClaimsPrincipal
                        message: text,
                        entityType: entityType,
                        entityId: entityId ? parseInt(entityId) : null,
                        mentions: mentions.map(m => ({ type: m.type, id: m.id })),
                        projectContextId: getActiveProjectContextId()
                    })
                });

                typingEl.remove();

                if (!resp.ok) {
                    const err = await resp.text();
                    appendMessage('ai', `⚠ Error: ${err || 'AI service unavailable. Please try again.'}`);
                    return;
                }

                const data = await resp.json();
                appendMessage('ai', data.message ?? '(no response)');

                mentions = [];
                updateBackdrop();

                if (data.actions?.length) {
                    renderActions(data.actions);
                } else {
                    actionsEl.style.display = 'none';
                }
            } catch (err) {
                typingEl.remove();
                appendMessage('ai', `⚠ Network error: ${err.message}. Please check your connection.`);
            } finally {
                isSending = false;
                sendBtn.disabled = inputEl.value.length === 0;
            }
        }

        function handleSlashCommand(cmdText) {
            const parts = cmdText.split(' ');
            const cmd = parts[0].toLowerCase();
            appendMessage('user', cmdText);

            switch (cmd) {
                case '/help':
                    appendMessage('ai', `<strong>Available AI Copilot Commands:</strong><br>
                    <ul>
                        <li><strong>/help</strong> — Lists all available commands and features.</li>
                        <li><strong>/capacity</strong> — Navigate to the capacity planning analytics view.</li>
                        <li><strong>/pert</strong> — Explains the PERT three-point estimate system.</li>
                        <li><strong>/reestimate</strong> — Guidelines for using bulk re-estimation tools.</li>
                    </ul><br>
                    You can also type <strong>@</strong> in the input area to mention projects, epics, features, user stories, tasks, sprints, or users directly in your conversation context.`);
                    break;
                case '/capacity':
                    appendMessage('ai', `🔄 Redirecting you to the Capacity Planning Dashboard...`);
                    setTimeout(() => {
                        window.location.href = '/Capacity';
                    }, 1000);
                    break;
                case '/pert':
                    appendMessage('ai', `<strong>PERT Three-Point Estimation System</strong><br><br>
                    PERT calculates the expected duration of tasks using three estimates:<br>
                    <ul>
                        <li><strong>O</strong>: Optimistic Estimate (best-case duration)</li>
                        <li><strong>M</strong>: Most Likely Estimate (average/normal duration)</li>
                        <li><strong>P</strong>: Pessimistic Estimate (worst-case duration)</li>
                    </ul><br>
                    Formula:<br>
                    \\[ \\mu = \\frac{O + 4M + P}{6} \\]<br>
                    Standard Deviation (uncertainty):<br>
                    \\[ \\sigma = \\frac{P - O}{6} \\]<br><br>
                    OfficeTaskManagement automatically applies this formula to all estimations generated by the AI or saved by you, ensuring risk-adjusted capacity planning.`);
                    break;
                case '/reestimate':
                    appendMessage('ai', `<strong>Bulk AI Re-estimation Guidelines</strong><br><br>
                    To re-estimate tasks with AI:<br>
                    <ol>
                        <li>Navigate to the Tasks list page.</li>
                        <li>Select multiple tasks using the check-boxes in the task table.</li>
                        <li>Click the <strong>"Re-estimate Selected Tasks with AI"</strong> button on the task toolbar.</li>
                        <li>Review the AI estimations and click confirm to save them.</li>
                    </ol>`);
                    break;
                default:
                    appendMessage('ai', `⚠ Unknown command: <strong>${cmd}</strong>. Type <strong>/help</strong> to see the list of available commands.`);
                    break;
            }
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
            { name: '/help', desc: 'Display AI Copilot capabilities and available tools', icon: '❓' },
            { name: '/capacity', desc: 'Query resource availability and capacity metrics', icon: '🏃' },
            { name: '/pert', desc: 'Show the PERT estimation formula explanation', icon: '📊' },
            { name: '/reestimate', desc: 'Get guidelines for bulk AI re-estimation of tasks', icon: '🔄' }
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
