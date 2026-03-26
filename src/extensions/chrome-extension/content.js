let suggestionsDiv = null;
let activeElement = null;
let tasks = [];
let filteredTasks = [];
let selectedIndex = 0;
let mentionQuery = "";

const createSuggestionsUI = () => {
    suggestionsDiv = document.createElement('div');
    suggestionsDiv.className = 'taskflow-suggestions';
    document.body.appendChild(suggestionsDiv);
};

const showSuggestions = (rect) => {
    if (!suggestionsDiv) createSuggestionsUI();
    suggestionsDiv.style.top = `${window.scrollY + rect.top + rect.height + 5}px`;
    suggestionsDiv.style.left = `${window.scrollX + rect.left}px`;
    suggestionsDiv.classList.add('visible');
    renderSuggestions();
};

const hideSuggestions = () => {
    if (suggestionsDiv) suggestionsDiv.classList.remove('visible');
    selectedIndex = 0;
};

const renderSuggestions = () => {
    if (!suggestionsDiv) return;
    suggestionsDiv.innerHTML = '';
    
    if (filteredTasks.length === 0) {
        suggestionsDiv.innerHTML = '<div style="padding:10px; opacity:0.6">No matching tasks</div>';
        return;
    }

    filteredTasks.forEach((task, index) => {
        const item = document.createElement('div');
        item.className = `taskflow-suggestion-item ${index === selectedIndex ? 'selected' : ''}`;
        item.innerHTML = `
            <span class="title">#${task.id} ${task.title}</span>
            <span class="meta">${task.projectName || 'Independent'} - ${task.statusName}</span>
        `;
        item.onclick = () => selectTask(task);
        suggestionsDiv.appendChild(item);
    });
};

const selectTask = (task) => {
    if (!activeElement) return;

    const text = activeElement.value;
    const pos = activeElement.selectionStart;
    const lastHash = text.lastIndexOf('#', pos - 1);
    
    const before = text.substring(0, lastHash);
    const after = text.substring(pos);
    const url = `http://localhost:5035/TaskItems/Details/${task.id}`;
    const insert = `[#${task.id}](${url})`;
    
    activeElement.value = before + insert + after;
    activeElement.selectionStart = activeElement.selectionEnd = before.length + insert.length;
    
    // Trigger input event for frameworks like React/Vue
    activeElement.dispatchEvent(new Event('input', { bubbles: true }));
    hideSuggestions();
};

const handleInput = (e) => {
    const el = e.target;
    if (el.tagName !== 'TEXTAREA' && (el.tagName !== 'INPUT' || el.type !== 'text')) return;
    activeElement = el;

    const text = el.value;
    const pos = el.selectionStart;
    const lastHash = text.lastIndexOf('#', pos - 1);
    
    if (lastHash !== -1) {
        const query = text.substring(lastHash + 1, pos);
        // Only allow word segments (no spaces)
        if (query.includes(' ') || query.includes('\n')) {
            hideSuggestions();
            return;
        }

        mentionQuery = query;
        chrome.runtime.sendMessage({ type: 'getTasks' }, (response) => {
            if (response && response.tasks) {
                tasks = response.tasks;
                filterTasks();
                if (filteredTasks.length > 0 || mentionQuery.length >= 0) {
                    const rect = el.getBoundingClientRect();
                    showSuggestions(rect);
                }
            }
        });
    } else {
        hideSuggestions();
    }
};

const filterTasks = () => {
    if (!mentionQuery) {
        filteredTasks = tasks.slice(0, 10);
    } else {
        const q = mentionQuery.toLowerCase();
        filteredTasks = tasks.filter(t => 
            String(t.id).includes(q) || t.title.toLowerCase().includes(q)
        ).slice(0, 10);
    }
    selectedIndex = 0;
};

const handleKeydown = (e) => {
    if (!suggestionsDiv || !suggestionsDiv.classList.contains('visible')) return;

    if (e.key === 'ArrowDown') {
        e.preventDefault();
        selectedIndex = (selectedIndex + 1) % filteredTasks.length;
        renderSuggestions();
    } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        selectedIndex = (selectedIndex - 1 + filteredTasks.length) % filteredTasks.length;
        renderSuggestions();
    } else if (e.key === 'Enter') {
        if (filteredTasks.length > 0) {
            e.preventDefault();
            selectTask(filteredTasks[selectedIndex]);
        }
    } else if (e.key === 'Escape') {
        hideSuggestions();
    }
};

document.addEventListener('input', handleInput);
document.addEventListener('keydown', handleKeydown);
document.addEventListener('click', (e) => {
    if (suggestionsDiv && !suggestionsDiv.contains(e.target)) {
        hideSuggestions();
    }
});
