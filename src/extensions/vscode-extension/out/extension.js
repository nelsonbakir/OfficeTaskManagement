"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.activate = activate;
exports.deactivate = deactivate;
const vscode = require("vscode");
const apiService_1 = require("./apiService");
function activate(context) {
    console.log('TaskFlow Extension is now active!');
    const apiService = new apiService_1.ApiService();
    // Check for existing token securely (mocking global state as vault for scaffolding)
    const token = context.globalState.get('taskflow.token');
    if (token) {
        apiService.setToken(token);
    }
    const provider = new TaskFlowSidebarProvider(context.extensionUri, apiService, context);
    context.subscriptions.push(vscode.window.registerWebviewViewProvider('taskflow.dashboardView', provider));
    let authCmd = vscode.commands.registerCommand('taskflow.authenticate', async () => {
        const email = await vscode.window.showInputBox({ placeHolder: 'john.doe@contoso.com', prompt: 'Enter your TaskFlow Email' });
        if (!email)
            return;
        const password = await vscode.window.showInputBox({ prompt: 'Enter your TaskFlow Password', password: true });
        if (!password)
            return;
        try {
            const result = await apiService.login(email, password);
            context.globalState.update('taskflow.token', result.token);
            apiService.setToken(result.token);
            vscode.window.showInformationMessage(`TaskFlow: Successfully authenticated as ${result.user.fullName}`);
            await updateTasksCache();
            provider.refresh();
        }
        catch (error) {
            vscode.window.showErrorMessage('TaskFlow Authentication failed: ' + error.message);
        }
    });
    let mapCmd = vscode.commands.registerCommand('taskflow.mapProject', async () => {
        if (!apiService.hasToken()) {
            vscode.window.showErrorMessage('TaskFlow: Please authenticate first.');
            return;
        }
        try {
            const projects = await apiService.getProjects();
            const items = projects.map(p => ({ label: p.name, detail: p.description, id: p.id }));
            const selected = await vscode.window.showQuickPick(items, { placeHolder: 'Link this VS Code workspace to a TaskFlow Project' });
            if (selected) {
                // Pin project to this specific workspace root
                context.workspaceState.update('taskflow.projectId', selected.id);
                vscode.window.showInformationMessage(`TaskFlow: Ext. mapped successfully to ${selected.label}`);
                await updateTasksCache();
                provider.refresh();
            }
        }
        catch (error) {
            vscode.window.showErrorMessage('TaskFlow: Failed to fetch projects from server.');
        }
    });
    let refreshCmd = vscode.commands.registerCommand('taskflow.refresh', async () => {
        await updateTasksCache();
        provider.refresh();
    });
    let signoutCmd = vscode.commands.registerCommand('taskflow.signout', async () => {
        context.globalState.update('taskflow.token', undefined);
        apiService.clearToken();
        vscode.window.showInformationMessage('TaskFlow: Signed out successfully.');
        provider.refresh();
    });
    let tasksCache = [];
    let lastProjectId = -2;
    async function updateTasksCache() {
        try {
            const projectId = context.workspaceState.get('taskflow.projectId');
            tasksCache = await apiService.getTasks(projectId);
            lastProjectId = projectId;
        }
        catch (e) {
            console.error('TaskFlow: Failed to pre-fetch tasks', e);
        }
    }
    // Initial pre-fetch
    if (apiService.hasToken()) {
        updateTasksCache();
    }
    let commitProvider = vscode.languages.registerCompletionItemProvider([{ language: 'git-commit' }, { scheme: 'vscode-scm' }], {
        provideCompletionItems(document, position) {
            if (!apiService.hasToken())
                return undefined;
            const linePrefix = document.lineAt(position).text.substr(0, position.character);
            const lastHashIndex = linePrefix.lastIndexOf('#');
            if (lastHashIndex === -1)
                return undefined;
            const textAfterHash = linePrefix.substring(lastHashIndex + 1);
            // Only allow digits after '#' for now to match user expectation of "task search pattern"
            if (textAfterHash.length > 0 && !/^\d*$/.test(textAfterHash))
                return undefined;
            if (textAfterHash.includes(' '))
                return undefined;
            const range = new vscode.Range(new vscode.Position(position.line, lastHashIndex), position);
            const items = tasksCache.map(t => {
                const item = new vscode.CompletionItem(`#${t.id} ${t.title}`, vscode.CompletionItemKind.Issue);
                item.insertText = `[#${t.id}](http://localhost:5035/TaskItems/Details/${t.id})`;
                item.filterText = `#${t.id}${t.title}`;
                item.detail = `${t.projectName || 'Independent'} - ${t.statusName}`;
                item.documentation = new vscode.MarkdownString(t.description || 'No description.');
                item.range = range;
                return item;
            });
            return new vscode.CompletionList(items, true);
        }
    }, '#', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
    vscode.commands.registerCommand('taskflow.updateCache', async () => {
        await updateTasksCache();
    });
    context.subscriptions.push(authCmd, mapCmd, refreshCmd, signoutCmd, commitProvider);
    // Notification Polling (Every 30 seconds)
    setInterval(async () => {
        if (apiService.hasToken()) {
            try {
                const notifications = await apiService.getNotifications();
                if (notifications && notifications.length > 0) {
                    for (const notif of notifications) {
                        vscode.window.showInformationMessage(`TaskFlow: ${notif.title}\n${notif.message}`, 'Mark as Read')
                            .then(selection => {
                            if (selection === 'Mark as Read') {
                                apiService.markNotificationRead(notif.id).then(() => {
                                    provider.refresh();
                                });
                            }
                        });
                    }
                }
            }
            catch (err) {
                // Silently swallow background polling errors
            }
        }
    }, 30000);
}
class TaskFlowSidebarProvider {
    constructor(_extensionUri, _apiService, _context) {
        this._extensionUri = _extensionUri;
        this._apiService = _apiService;
        this._context = _context;
    }
    resolveWebviewView(webviewView) {
        this._view = webviewView;
        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._extensionUri]
        };
        webviewView.webview.onDidReceiveMessage(async (data) => {
            switch (data.type) {
                case 'updateStatus': {
                    if (!this._apiService.hasToken())
                        return;
                    try {
                        await this._apiService.updateTaskStatus(data.taskId, data.newStatus);
                        vscode.window.showInformationMessage('TaskFlow: Task synchronized remotely!');
                        this.refresh();
                    }
                    catch (err) {
                        if (err instanceof apiService_1.ApiServiceError && err.code === 'UNAUTHORIZED') {
                            vscode.window.showErrorMessage('TaskFlow: Session expired. Please re-authenticate.');
                        }
                        else if (err instanceof apiService_1.ApiServiceError && err.code === 'NETWORK') {
                            vscode.window.showErrorMessage('TaskFlow: API is unreachable.');
                        }
                        else {
                            vscode.window.showErrorMessage('TaskFlow: Failed to update task status.');
                        }
                    }
                    break;
                }
                case 'signout': {
                    vscode.commands.executeCommand('taskflow.signout');
                    break;
                }
                case 'changeProject': {
                    if (data.projectId === 0) {
                        this._context.workspaceState.update('taskflow.projectId', undefined);
                    }
                    else {
                        this._context.workspaceState.update('taskflow.projectId', data.projectId);
                    }
                    this.refresh();
                    break;
                }
                case 'authenticate': {
                    vscode.commands.executeCommand('taskflow.authenticate');
                    break;
                }
                case 'mapProject': {
                    vscode.commands.executeCommand('taskflow.mapProject');
                    break;
                }
                case 'showDetails': {
                    this._currentTaskId = data.taskId;
                    this.refresh();
                    break;
                }
                case 'hideDetails': {
                    this._currentTaskId = undefined;
                    this.refresh();
                    break;
                }
                case 'postComment': {
                    if (this._currentTaskId) {
                        try {
                            await this._apiService.postComment(this._currentTaskId, data.text);
                            vscode.window.showInformationMessage('TaskFlow: Comment posted.');
                            this.refresh();
                        }
                        catch (err) {
                            vscode.window.showErrorMessage('TaskFlow: Failed to post comment.');
                        }
                    }
                    break;
                }
                case 'openInPortal': {
                    try {
                        const url = await this._apiService.getPortalLink(this._currentTaskId);
                        vscode.env.openExternal(vscode.Uri.parse('http://localhost:5035' + url));
                    }
                    catch (err) {
                        vscode.window.showErrorMessage('TaskFlow: Failed to generate portal link.');
                    }
                    break;
                }
                case 'refresh': {
                    this.refresh();
                    break;
                }
            }
        });
        this.refresh();
    }
    async refresh() {
        if (!this._view)
            return;
        // The commands already call updateTasksCache, but we can be extra safe here
        // No need to await here to avoid blocking UI
        vscode.commands.executeCommand('taskflow.updateCache');
        if (!this._apiService.hasToken()) {
            this._view.webview.html = this._getHtmlForUnauth();
            return;
        }
        const currentProjectId = this._context.workspaceState.get('taskflow.projectId');
        try {
            if (this._currentTaskId) {
                const task = await this._apiService.getTaskDetails(this._currentTaskId);
                const comments = await this._apiService.getComments(this._currentTaskId);
                const users = await this._apiService.getEligibleUsers();
                this._view.webview.html = this._getHtmlForTaskDetails(task, comments, users);
            }
            else {
                const tasks = await this._apiService.getTasks(currentProjectId);
                const projects = await this._apiService.getProjects();
                this._view.webview.html = this._getHtmlForTasks(tasks, projects, currentProjectId);
            }
        }
        catch (err) {
            this._currentTaskId = undefined;
            if (err instanceof apiService_1.ApiServiceError && err.code === 'UNAUTHORIZED') {
                this._apiService.clearToken();
                this._context.globalState.update('taskflow.token', undefined);
                this._view.webview.html = this._getHtmlForError('Session Expired', 'Your session has expired. Please re-authenticate.', true);
            }
            else if (err instanceof apiService_1.ApiServiceError && err.code === 'NETWORK') {
                this._view.webview.html = this._getHtmlForError('API Unreachable', 'Cannot connect to the TaskFlow server. Ensure the API is running on localhost:5035.');
            }
            else if (err instanceof apiService_1.ApiServiceError && err.code === 'NOT_FOUND') {
                this._view.webview.html = this._getHtmlForError('Not Found', 'The requested task no longer exists.');
            }
            else {
                this._view.webview.html = this._getHtmlForError('Something went wrong', 'An unexpected error occurred. Please try refreshing.');
            }
        }
    }
    _getHtmlForError(title, message, showAuth = false) {
        const authButton = showAuth
            ? `<button onclick="auth()" style="background:var(--vscode-button-background);color:var(--vscode-button-foreground);border:none;padding:8px 16px;border-radius:2px;cursor:pointer;font-size:12px;margin-top:12px;">Re-authenticate</button>`
            : `<button onclick="refresh()" style="background:transparent;border:1px solid var(--vscode-widget-border);color:var(--vscode-textLink-foreground);padding:6px 14px;border-radius:2px;cursor:pointer;font-size:12px;margin-top:12px;">Retry</button>`;
        return `<!DOCTYPE html><html lang="en"><head><meta charset="UTF-8"></head><body style="font-family:var(--vscode-font-family);padding:20px;text-align:center;">
            <h4 style="color:var(--vscode-errorForeground);margin-bottom:8px;">${title}</h4>
            <p style="font-size:12px;color:var(--vscode-descriptionForeground);">${message}</p>
            ${authButton}
            <script>
                const vscode = acquireVsCodeApi();
                function auth() { vscode.postMessage({ type: 'authenticate' }); }
                function refresh() { vscode.postMessage({ type: 'refresh' }); }
            </script>
        </body></html>`;
    }
    _getHtmlForUnauth() {
        return `
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>TaskFlow Connect</title>
                <style>
                    body { font-family: var(--vscode-font-family); padding: 2rem 1rem; text-align: center; color: var(--vscode-foreground); }
                    button { background-color: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; padding: 10px 20px; font-weight: 500; font-size: 13px; border-radius: 2px; cursor: pointer; margin-top: 20px; transition: 0.2s; width: 100%;}
                    button:hover { background-color: var(--vscode-button-hoverBackground); }
                    .splash { background: var(--vscode-editor-inactiveSelectionBackground); width: 64px; height: 64px; border-radius: 50%; margin: 0 auto 1.5rem; display: flex; align-items:center; justify-content:center; }
                </style>
            </head>
            <body>
                <div class="splash">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="feather feather-trello"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect><rect x="7" y="7" width="3" height="9"></rect><rect x="14" y="7" width="3" height="5"></rect></svg>
                </div>
                <h3 style="margin-bottom: 0.5rem; font-weight: 600;">Welcome to TaskFlow</h3>
                <p style="font-size: 13px; color: var(--vscode-descriptionForeground);">Authentication required to pull your assigned items and notifications.</p>
                <button onclick="auth()">Authenticate</button>
                <script>
                    const vscode = acquireVsCodeApi();
                    function auth() {
                        vscode.postMessage({ type: 'authenticate' });
                    }
                </script>
            </body>
            </html>
        `;
    }
    _getHtmlForTasks(tasks, projects, currentProjectId) {
        let taskItems = '';
        if (tasks.length === 0) {
            taskItems = `
                <div style="text-align:center; padding: 2rem 1rem; color: var(--vscode-descriptionForeground);">
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="margin-bottom: 10px; opacity: 0.5;"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>
                    <p style="font-size: 12px;">Inbox zero! No active assignments found.</p>
                </div>`;
        }
        else {
            tasks.forEach(t => {
                const isDone = t.status === 6;
                taskItems += `
                <div style="border: 1px solid var(--vscode-widget-border); border-radius: 6px; padding: 12px; margin-bottom: 12px; background: ${isDone ? 'var(--vscode-list-hoverBackground)' : 'var(--vscode-editor-background)'}; transition: transform 0.2s;">
                    <div style="display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 8px;">
                        <h4 style="margin: 0; font-size: 13px; font-weight: 600; color: var(--vscode-editor-foreground); line-height: 1.4;">${t.title}</h4>
                    </div>
                    
                    <div style="display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 12px;">
                        <span style="font-size: 10px; background: var(--vscode-badge-background); color: var(--vscode-badge-foreground); padding: 2px 6px; border-radius: 10px;">${t.projectName || 'Independent'}</span>
                        <span style="font-size: 10px; border: 1px solid var(--vscode-widget-border); padding: 2px 6px; border-radius: 10px; color: var(--vscode-descriptionForeground);">${t.priority}</span>
                    </div>
                    
                    <div style="display: flex; align-items: center; justify-content: space-between;">
                        <label style="font-size: 11px; font-weight: 500; color: var(--vscode-descriptionForeground);">STATUS</label>
                        <select onchange="updateStatus(${t.id}, this.value)" style="padding: 4px; border-radius: 4px; border: 1px solid var(--vscode-input-border); font-size: 11px; font-family: inherit; background: var(--vscode-input-background); color: var(--vscode-input-foreground); cursor: pointer; min-width: 100px;">
                            <option value="0" ${t.status === 0 ? 'selected' : ''}>New</option>
                            <option value="1" ${t.status === 1 ? 'selected' : ''}>Approved</option>
                            <option value="2" ${t.status === 2 ? 'selected' : ''}>To Do</option>
                            <option value="3" ${t.status === 3 ? 'selected' : ''}>In Progress</option>
                            <option value="4" ${t.status === 4 ? 'selected' : ''}>Committed</option>
                            <option value="5" ${t.status === 5 ? 'selected' : ''}>Tested</option>
                            <option value="6" ${t.status === 6 ? 'selected' : ''}>Done</option>
                        </select>
                        <button onclick="showDetails(${t.id})" style="background:transparent; border:none; color:var(--vscode-textLink-foreground); cursor:pointer; font-size:11px; padding: 4px;">Details</button>
                    </div>
                </div>
                `;
            });
        }
        const projectOptions = projects.map(p => `<option value="${p.id}" ${p.id === currentProjectId ? 'selected' : ''}>${p.name}</option>`).join('');
        const projectSelector = `
            <div style="margin-bottom: 15px;">
                <label style="font-size: 11px; font-weight: 600; color: var(--vscode-descriptionForeground); display: block; margin-bottom: 4px; text-transform: uppercase;">Workspace Filter</label>
                <select onchange="changeProject(this.value)" style="width: 100%; padding: 6px; border-radius: 4px; border: 1px solid var(--vscode-input-border); background: var(--vscode-input-background); color: var(--vscode-input-foreground); font-size: 12px; cursor: pointer;">
                    <option value="0" ${!currentProjectId ? 'selected' : ''}>-- All Projects --</option>
                    ${projectOptions}
                </select>
            </div>
        `;
        return `
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>TaskFlow Tasks</title>
                <style>
                    body { font-family: var(--vscode-font-family); padding: 12px; background: var(--vscode-sideBar-background); }
                    select:focus { outline: 1px solid var(--vscode-focusBorder); border-color: transparent;}
                    button:hover { opacity: 0.8; }
                </style>
            </head>
            <body>
                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px;">
                    <h2 style="margin: 0; font-size: 12px; text-transform: uppercase; font-weight: 600; letter-spacing: 0.5px; color: var(--vscode-sideBarTitle-foreground);">My Assignments</h2>
                    <div style="display: flex; gap: 8px;">
                        <button onclick="refresh()" style="background:transparent; border:none; color:var(--vscode-textLink-foreground); cursor:pointer; padding:0; display:flex; align-items:center;" title="Refresh">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 4 23 10 17 10"></polyline><polyline points="1 20 1 14 7 14"></polyline><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path></svg>
                        </button>
                        <button onclick="signout()" style="background:transparent; border:none; color:var(--vscode-errorForeground); cursor:pointer; padding:0; display:flex; align-items:center;" title="Sign Out">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"></path><polyline points="16 17 21 12 16 7"></polyline><line x1="21" y1="12" x2="9" y2="12"></line></svg>
                        </button>
                    </div>
                </div>
                
                <button onclick="openPortal()" style="width: 100%; background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; padding: 8px; font-size: 12px; border-radius: 2px; margin-bottom: 15px; cursor: pointer;">Open in Portal</button>
                
                ${projectSelector}
                ${taskItems}

                <script>
                    const vscode = acquireVsCodeApi();
                    function updateStatus(taskId, statusVal) {
                        vscode.postMessage({ type: 'updateStatus', taskId: taskId, newStatus: parseInt(statusVal) });
                    }
                    function changeProject(projectId) {
                        vscode.postMessage({ type: 'changeProject', projectId: parseInt(projectId) });
                    }
                    function showDetails(taskId) {
                        vscode.postMessage({ type: 'showDetails', taskId: taskId });
                    }
                    function openPortal() {
                        vscode.postMessage({ type: 'openInPortal' });
                    }
                    function refresh() {
                        vscode.postMessage({ type: 'refresh' });
                    }
                    function signout() {
                        vscode.postMessage({ type: 'signout' });
                    }
                </script>
            </body>
            </html>
        `;
    }
    _getHtmlForTaskDetails(task, comments, users) {
        let commentsHtml = comments.map(c => `
            <div style="display: flex; flex-direction: column; margin-bottom: 12px; align-items: ${c.isSelf ? 'flex-end' : 'flex-start'};">
                <span style="font-size: 9px; color: var(--vscode-descriptionForeground); margin-bottom: 4px; margin-${c.isSelf ? 'right' : 'left'}: 4px;">${c.isSelf ? 'You' : c.authorName} &middot; ${new Date(c.createdAt).toLocaleString()}</span>
                <div style="background: ${c.isSelf ? 'var(--vscode-button-background)' : 'var(--vscode-editor-inactiveSelectionBackground)'}; padding: 8px 12px; border-radius: 14px; border-top-${c.isSelf ? 'right' : 'left'}-radius: 4px; font-size: 11px; color: ${c.isSelf ? 'var(--vscode-button-foreground)' : 'var(--vscode-editor-foreground)'}; max-width: 90%; line-height: 1.4;">
                    ${c.commentText}
                </div>
            </div>
        `).join('');
        if (comments.length === 0) {
            commentsHtml = `<p style="font-size: 11px; color: var(--vscode-descriptionForeground);">No comments yet.</p>`;
        }
        return `
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>Task Details</title>
                <style>
                    body { font-family: var(--vscode-font-family); padding: 12px; background: var(--vscode-sideBar-background); }
                    button { cursor: pointer; }
                </style>
            </head>
            <body>
                <div style="margin-bottom: 16px;">
                    <button onclick="back()" style="background:transparent; border:none; color:var(--vscode-textLink-foreground); padding:0; font-size:12px; margin-bottom: 8px;">&larr; Back to Tasks</button>
                    <h2 style="margin: 0 0 8px 0; font-size: 14px; font-weight: 600; color: var(--vscode-editor-foreground); line-height: 1.4;">${task.title}</h2>
                    <div style="display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 12px;">
                        <span style="font-size: 10px; background: var(--vscode-badge-background); color: var(--vscode-badge-foreground); padding: 2px 6px; border-radius: 10px;">${task.projectName || 'Independent'}</span>
                        <span style="font-size: 10px; border: 1px solid var(--vscode-widget-border); padding: 2px 6px; border-radius: 10px; color: var(--vscode-descriptionForeground);">${task.priority}</span>
                    </div>
                </div>

                <div style="font-size: 12px; color: var(--vscode-editor-foreground); margin-bottom: 16px; line-height: 1.5; white-space: pre-wrap; background: var(--vscode-editor-inactiveSelectionBackground); padding: 10px; border-radius: 4px;">${task.description || 'No description provided.'}</div>

                <button onclick="openPortal()" style="width: 100%; background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; padding: 8px; font-size: 12px; border-radius: 2px; margin-bottom: 20px;">For more detail navigate to the portal</button>

                <h3 style="font-size: 12px; font-weight: 600; margin-bottom: 12px; text-transform: uppercase;">Discussions</h3>
                <div style="margin-bottom: 16px; display: flex; flex-direction: column-reverse; max-height: 250px; overflow-y: auto; padding-right: 8px;">
                    ${commentsHtml}
                </div>

                <div style="position: relative;">
                    <textarea id="commentBox" placeholder="Add a comment... Type @ to mention someone" style="width: 100%; min-height: 60px; padding: 8px; box-sizing: border-box; background: var(--vscode-input-background); color: var(--vscode-input-foreground); border: 1px solid var(--vscode-input-border); border-radius: 4px; font-family: inherit; font-size: 12px; margin-bottom: 8px;"></textarea>
                    <ul id="mentionDropdown" style="display: none; position: absolute; bottom: 100%; left: 0; background: var(--vscode-dropdown-background); border: 1px solid var(--vscode-dropdown-border); list-style: none; padding: 0; margin: 0; border-radius: 4px; width: 100%; max-height: 150px; overflow-y: auto; z-index: 10;"></ul>
                </div>
                <button onclick="postComment()" style="width: 100%; background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; padding: 6px; font-size: 12px; border-radius: 2px;">Post Comment</button>

                <script>
                    const vscode = acquireVsCodeApi();
                    const users = ${JSON.stringify(users)};
                    
                    function back() { vscode.postMessage({ type: 'hideDetails' }); }
                    function openPortal() { vscode.postMessage({ type: 'openInPortal' }); }
                    function postComment() { 
                        const text = document.getElementById('commentBox').value;
                        if(text.trim()) {
                            vscode.postMessage({ type: 'postComment', text: text }); 
                        }
                    }

                    const commentBox = document.getElementById('commentBox');
                    const mentionDropdown = document.getElementById('mentionDropdown');
                    
                    let mentionStartIndex = -1;

                    commentBox.addEventListener('input', (e) => {
                        const val = commentBox.value;
                        const cursor = commentBox.selectionStart;
                        
                        // Find the last '@' before the cursor
                        mentionStartIndex = -1;
                        for(let i = cursor - 1; i >= 0; i--) {
                            if (val[i] === '@') {
                                // Must be at start of string or preceded by space
                                if (i === 0 || val[i-1] === ' ' || val[i-1] === '\\n') {
                                    mentionStartIndex = i;
                                }
                                break;
                            } else if (val[i] === ' ' || val[i] === '\\n') {
                                break; // Stop looking if we hit a space before '@'
                            }
                        }

                        if (mentionStartIndex !== -1) {
                            const query = val.slice(mentionStartIndex + 1, cursor).toLowerCase();
                            const matches = users.filter(u => u.display.toLowerCase().includes(query) || u.email.toLowerCase().includes(query));
                            
                            if (matches.length > 0) {
                                mentionDropdown.innerHTML = '';
                                matches.forEach(match => {
                                    const li = document.createElement('li');
                                    li.style.padding = '8px';
                                    li.style.cursor = 'pointer';
                                    li.style.borderBottom = '1px solid var(--vscode-widget-border)';
                                    li.style.fontSize = '12px';
                                    li.style.color = 'var(--vscode-dropdown-foreground)';
                                    li.innerHTML = '<strong style="display:block;">' + match.display + '</strong><span style="font-size:10px; opacity:0.7;">' + match.email + '</span>';
                                    
                                    li.onmouseenter = () => li.style.background = 'var(--vscode-list-hoverBackground)';
                                    li.onmouseleave = () => li.style.background = 'transparent';
                                    
                                    li.onclick = () => {
                                        const before = val.slice(0, mentionStartIndex);
                                        const after = val.slice(cursor);
                                        commentBox.value = before + '@' + match.display + ' ' + after;
                                        mentionDropdown.style.display = 'none';
                                        commentBox.focus();
                                    };
                                    mentionDropdown.appendChild(li);
                                });
                                mentionDropdown.style.display = 'block';
                            } else {
                                mentionDropdown.style.display = 'none';
                            }
                        } else {
                            mentionDropdown.style.display = 'none';
                        }
                    });

                    // Hide dropdown if clicked outside
                    document.addEventListener('click', (e) => {
                        if (e.target !== commentBox && e.target !== mentionDropdown) {
                            mentionDropdown.style.display = 'none';
                        }
                    });
                </script>
            </body>
            </html>
        `;
    }
}
function deactivate() { }
//# sourceMappingURL=extension.js.map