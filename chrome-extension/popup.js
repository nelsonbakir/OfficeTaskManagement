document.addEventListener('DOMContentLoaded', async () => {
    // Containers
    const authContainer = document.getElementById('auth-container');
    const mainContainer = document.getElementById('main-container');
    const detailContainer = document.getElementById('detail-container');
    
    // Auth elements
    const emailInput = document.getElementById('email');
    const passwordInput = document.getElementById('password');
    const loginBtn = document.getElementById('login-btn');
    const logoutBtn = document.getElementById('logout-btn');
    const authError = document.getElementById('auth-error');

    // List elements
    const refreshBtn = document.getElementById('refresh-btn');
    const projectFilter = document.getElementById('project-filter');
    const taskList = document.getElementById('task-list');
    const loader = document.getElementById('loader');

    // Detail elements
    const backBtn = document.getElementById('back-btn');
    const detailTitle = document.getElementById('detail-title');
    const detailDesc = document.getElementById('detail-desc');
    const commentList = document.getElementById('comment-list');
    const commentBox = document.getElementById('comment-box');
    const postCommentBtn = document.getElementById('post-comment-btn');
    const mentionDropdown = document.getElementById('mention-dropdown');
    const detailPortalBtn = document.getElementById('detail-portal-btn');

    let activeTaskId = null;
    let eligibleUsers = [];

    const statuses = [
        { id: 0, name: 'New' },
        { id: 1, name: 'Approved' },
        { id: 2, name: 'To Do' },
        { id: 3, name: 'In Progress' },
        { id: 4, name: 'Committed' },
        { id: 5, name: 'Tested' },
        { id: 6, name: 'Done' }
    ];

    const checkAuth = async () => {
        const token = await ApiService.getToken();
        if (token) {
            authContainer.classList.add('hidden');
            mainContainer.classList.remove('hidden');
            detailContainer.classList.add('hidden');
            loadInitialData();
        } else {
            authContainer.classList.remove('hidden');
            mainContainer.classList.add('hidden');
            detailContainer.classList.add('hidden');
        }
    };

    const loadInitialData = async () => {
        try {
            const projects = await ApiService.getProjects();
            projectFilter.innerHTML = '<option value="0">-- All Projects --</option>';
            projects.forEach(p => {
                const opt = document.createElement('option');
                opt.value = p.id;
                opt.textContent = p.name;
                projectFilter.appendChild(opt);
            });
            
            chrome.storage.local.get(['selectedProjectId'], (result) => {
                if (result.selectedProjectId) projectFilter.value = result.selectedProjectId;
                loadTasks();
            });
            
            eligibleUsers = await ApiService.getEligibleUsers();
        } catch (e) {
            console.error(e);
        }
    };

    const loadTasks = async () => {
        taskList.innerHTML = '';
        loader.classList.remove('hidden');
        try {
            const projectId = projectFilter.value;
            const tasks = await ApiService.getTasks(projectId);
            renderTasks(tasks);
        } catch (e) {
            taskList.innerHTML = '<div class="error-msg">Failed to load tasks</div>';
        } finally {
            loader.classList.add('hidden');
        }
    };

    const renderTasks = (tasks) => {
        if (tasks.length === 0) {
            taskList.innerHTML = '<div style="text-align:center; padding:20px; color:var(--secondary)">No assignments found</div>';
            return;
        }
        tasks.forEach(t => {
            const div = document.createElement('div');
            div.className = 'task-item';
            
            const statusOptions = statuses.map(s => 
                `<option value="${s.id}" ${t.status === s.id ? 'selected' : ''}>${s.name}</option>`
            ).join('');

            div.innerHTML = `
                <div class="task-title" style="cursor:pointer">${t.title}</div>
                <div class="task-meta">
                    <span class="tag">${t.projectName || 'Independent'}</span>
                </div>
                <div class="task-status-container">
                    <select class="task-status-select" data-id="${t.id}">
                        ${statusOptions}
                    </select>
                    <button class="text-btn details-link" style="margin-left:auto">Details &rarr;</button>
                </div>
            `;
            
            div.querySelector('.task-title').onclick = () => showTaskDetails(t.id);
            div.querySelector('.details-link').onclick = () => showTaskDetails(t.id);
            
            const select = div.querySelector('.task-status-select');
            select.addEventListener('change', async (e) => {
                try {
                    await ApiService.updateTaskStatus(t.id, parseInt(e.target.value));
                    chrome.runtime.sendMessage({ type: 'updateCache' });
                } catch (e) {
                    alert('Failed to update status');
                    loadTasks();
                }
            });

            taskList.appendChild(div);
        });
    };

    const showTaskDetails = async (taskId) => {
        activeTaskId = taskId;
        mainContainer.classList.add('hidden');
        detailContainer.classList.remove('hidden');
        detailTitle.textContent = 'Loading...';
        detailDesc.textContent = '';
        commentList.innerHTML = '';
        
        try {
            const task = await ApiService.getTaskDetails(taskId);
            detailTitle.textContent = task.title;
            detailDesc.textContent = task.description || 'No description provided.';
            detailPortalBtn.onclick = () => window.open(`http://localhost:5035/TaskItems/Details/${taskId}`, '_blank');
            
            loadComments(taskId);
        } catch (e) {
            detailTitle.textContent = 'Error loading task';
        }
    };

    const loadComments = async (taskId) => {
        try {
            const comments = await ApiService.getComments(taskId);
            renderComments(comments);
        } catch (e) {
            commentList.innerHTML = '<p class="error-msg">Failed to load comments</p>';
        }
    };

    const renderComments = (comments) => {
        commentList.innerHTML = '';
        if (comments.length === 0) {
            commentList.innerHTML = '<p style="font-size:11px; opacity:0.6">No comments yet.</p>';
            return;
        }
        comments.forEach(c => {
            const div = document.createElement('div');
            div.className = `comment-item ${c.isSelf ? 'self' : ''}`;
            div.innerHTML = `
                <div class="comment-author">${c.authorName} • ${new Date(c.createdAt).toLocaleTimeString()}</div>
                <div class="comment-bubble">${c.commentText}</div>
            `;
            commentList.appendChild(div);
        });
        commentList.scrollTop = commentList.scrollHeight;
    };

    // Mentions logic
    commentBox.addEventListener('input', () => {
        const val = commentBox.value;
        const cursor = commentBox.selectionStart;
        const textBefore = val.slice(0, cursor);
        const lastAt = textBefore.lastIndexOf('@');
        
        if (lastAt !== -1 && (lastAt === 0 || textBefore[lastAt-1] === ' ' || textBefore[lastAt-1] === '\n')) {
            const query = textBefore.slice(lastAt + 1).toLowerCase();
            const matches = eligibleUsers.filter(u => u.display.toLowerCase().includes(query) || u.email.toLowerCase().includes(query));
            
            if (matches.length > 0) {
                renderMentions(matches, lastAt, cursor);
            } else {
                mentionDropdown.classList.add('hidden');
            }
        } else {
            mentionDropdown.classList.add('hidden');
        }
    });

    const renderMentions = (matches, startIndex, cursorIndex) => {
        mentionDropdown.innerHTML = '';
        matches.slice(0, 5).forEach(m => {
            const div = document.createElement('div');
            div.className = 'mention-item';
            div.innerHTML = `<strong>${m.display}</strong><span>${m.email}</span>`;
            div.onclick = () => {
                const before = commentBox.value.slice(0, startIndex);
                const after = commentBox.value.slice(cursorIndex);
                commentBox.value = before + '@' + m.display + ' ' + after;
                mentionDropdown.classList.add('hidden');
                commentBox.focus();
            };
            mentionDropdown.appendChild(div);
        });
        mentionDropdown.classList.remove('hidden');
    };

    // Navigation and Events
    document.querySelectorAll('.nav-btn').forEach(btn => {
        btn.onclick = () => window.open(btn.dataset.url, '_blank');
    });

    backBtn.onclick = () => {
        activeTaskId = null;
        detailContainer.classList.add('hidden');
        mainContainer.classList.remove('hidden');
        loadTasks();
    };

    loginBtn.onclick = async () => {
        authError.textContent = '';
        try {
            await ApiService.login(emailInput.value, passwordInput.value);
            chrome.runtime.sendMessage({ type: 'updateCache' });
            checkAuth();
        } catch (e) {
            authError.textContent = 'Invalid credentials';
        }
    };

    logoutBtn.onclick = async () => {
        await ApiService.clearToken();
        chrome.runtime.sendMessage({ type: 'updateCache' });
        checkAuth();
    };

    refreshBtn.onclick = () => {
        chrome.runtime.sendMessage({ type: 'updateCache' }, loadTasks);
    };

    projectFilter.onchange = () => {
        chrome.storage.local.set({ selectedProjectId: projectFilter.value }, () => {
            chrome.runtime.sendMessage({ type: 'updateCache' }, loadTasks);
        });
    };

    postCommentBtn.onclick = async () => {
        const text = commentBox.value.trim();
        if (!text || !activeTaskId) return;
        
        postCommentBtn.disabled = true;
        try {
            await ApiService.postComment(activeTaskId, text);
            commentBox.value = '';
            loadComments(activeTaskId);
        } catch (e) {
            alert('Failed to post comment');
        } finally {
            postCommentBtn.disabled = false;
        }
    };

    checkAuth();
});
