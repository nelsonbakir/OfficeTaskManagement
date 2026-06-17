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

        // ── State ─────────────────────────────────────────────────────────────────
        // Read page context from meta tags injected by the layout for entity pages
        const entityType = document.querySelector('meta[name="ai-entity-type"]')?.content ?? null;
        const entityId   = document.querySelector('meta[name="ai-entity-id"]')?.content ?? null;

        // Unique conversation ID per browser session (persisted in sessionStorage)
        let conversationId = sessionStorage.getItem('copilot-conv-id') ?? generateConvId();
        sessionStorage.setItem('copilot-conv-id', conversationId);

        let isOpen    = false;
        let isSending = false;

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
        });

        inputEl.addEventListener('keydown', e => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                if (!sendBtn.disabled) sendMessage();
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
        });

        // ── Send message ──────────────────────────────────────────────────────────
        async function sendMessage() {
            const text = inputEl.value.trim();
            if (!text || isSending) return;

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
                        entityId: entityId ? parseInt(entityId) : null
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
            return text
                // Escape HTML first
                .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
                // Bold
                .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
                // Bullet lists
                .replace(/^- (.+)$/gm, '<li>$1</li>')
                .replace(/(<li>.*<\/li>)/s, '<ul>$1</ul>')
                // Numbered lists
                .replace(/^\d+\. (.+)$/gm, '<li>$1</li>')
                // Line breaks
                .replace(/\n/g, '<br>');
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
