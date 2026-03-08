import * as vscode from 'vscode';
import { ApiService } from './apiService';

export function activate(context: vscode.ExtensionContext) {
    console.log('TaskFlow Extension is now active!');

    const apiService = new ApiService();

    // Check for existing token securely (mocking global state as vault for scaffolding)
    const token = context.globalState.get<string>('taskflow.token');
    if (token) {
        apiService.setToken(token);
    }

    const provider = new TaskFlowSidebarProvider(context.extensionUri, apiService, context);
    context.subscriptions.push(
        vscode.window.registerWebviewViewProvider('taskflow.dashboardView', provider)
    );

    let authCmd = vscode.commands.registerCommand('taskflow.authenticate', async () => {
        const email = await vscode.window.showInputBox({ placeHolder: 'john.doe@contoso.com', prompt: 'Enter your TaskFlow Email' });
        if (!email) return;

        const password = await vscode.window.showInputBox({ prompt: 'Enter your TaskFlow Password', password: true });
        if (!password) return;

        try {
            const result = await apiService.login(email, password);
            context.globalState.update('taskflow.token', result.token);
            apiService.setToken(result.token);
            vscode.window.showInformationMessage(`TaskFlow: Successfully authenticated as ${result.user.fullName}`);
            provider.refresh();
        } catch (error: any) {
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
                provider.refresh();
            }
        } catch (error) {
            vscode.window.showErrorMessage('TaskFlow: Failed to fetch projects from server.');
        }
    });

    let refreshCmd = vscode.commands.registerCommand('taskflow.refresh', () => {
        provider.refresh();
    });

    context.subscriptions.push(authCmd, mapCmd, refreshCmd);

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
            } catch (err) {
                // Silently swallow background polling errors
            }
        }
    }, 30000);
}

class TaskFlowSidebarProvider implements vscode.WebviewViewProvider {
    private _view?: vscode.WebviewView;
    private _currentTaskId?: number;

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _apiService: ApiService,
        private readonly _context: vscode.ExtensionContext
    ) { }

    public resolveWebviewView(webviewView: vscode.WebviewView) {
        this._view = webviewView;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._extensionUri]
        };

        webviewView.webview.onDidReceiveMessage(async (data) => {
            switch (data.type) {
                case 'updateStatus': {
                    if (!this._apiService.hasToken()) return;
                    try {
                        await this._apiService.updateTaskStatus(data.taskId, data.newStatus);
                        vscode.window.showInformationMessage('TaskFlow: Task synchronized remotely!');
                        this.refresh();
                    } catch (err: any) {
                        vscode.window.showErrorMessage('TaskFlow: Authorization forbidden or backend unreachable.');
                    }
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
                        } catch (err) {
                            vscode.window.showErrorMessage('TaskFlow: Failed to post comment.');
                        }
                    }
                    break;
                }
                case 'openInPortal': {
                    try {
                        const url = await this._apiService.getPortalLink(this._currentTaskId);
                        vscode.env.openExternal(vscode.Uri.parse('http://localhost:5035' + url));
                    } catch (err) {
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

    public async refresh() {
        if (!this._view) return;

        if (!this._apiService.hasToken()) {
            this._view.webview.html = this._getHtmlForUnauth();
            return;
        }

        const projectId = this._context.workspaceState.get<number>('taskflow.projectId');

        try {
            if (this._currentTaskId) {
                const task = await this._apiService.getTaskDetails(this._currentTaskId);
                const comments = await this._apiService.getComments(this._currentTaskId);
                const users = await this._apiService.getEligibleUsers();
                this._view.webview.html = this._getHtmlForTaskDetails(task, comments, users);
            } else {
                const tasks = await this._apiService.getTasks(projectId);
                this._view.webview.html = this._getHtmlForTasks(tasks, projectId);
            }
        } catch (err) {
            this._currentTaskId = undefined;
            this._view.webview.html = `<body style="padding:20px; text-align:center;"><h4 style="color:var(--vscode-errorForeground);">Failed to fetch tasks</h4><p>Ensure API is online and tokens are unexpired.</p></body>`;
        }
    }

    private _getHtmlForUnauth() {
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

    private _getHtmlForTasks(tasks: any[], projectId?: number) {
        let taskItems = '';
        if (tasks.length === 0) {
            taskItems = `
                <div style="text-align:center; padding: 2rem 1rem; color: var(--vscode-descriptionForeground);">
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="margin-bottom: 10px; opacity: 0.5;"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>
                    <p style="font-size: 12px;">Inbox zero! No active assignments found.</p>
                </div>`;
        } else {
            tasks.forEach(t => {
                const isDone = t.status === 5;

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
                            <option value="1" ${t.status === 1 ? 'selected' : ''}>To Do</option>
                            <option value="2" ${t.status === 2 ? 'selected' : ''}>In Progress</option>
                            <option value="3" ${t.status === 3 ? 'selected' : ''}>Committed</option>
                            <option value="4" ${t.status === 4 ? 'selected' : ''}>Tested</option>
                            <option value="5" ${t.status === 5 ? 'selected' : ''}>Done</option>
                        </select>
                        <button onclick="showDetails(${t.id})" style="background:transparent; border:none; color:var(--vscode-textLink-foreground); cursor:pointer; font-size:11px; padding: 4px;">Details</button>
                    </div>
                </div>
                `;
            });
        }

        const projectNote = projectId
            ? `<div style="display: flex; justify-content: space-between; align-items:center; background: var(--vscode-editor-inactiveSelectionBackground); padding: 8px 12px; border-radius: 4px; margin-bottom: 15px;">
                 <span style="font-size: 11px; font-weight: 500; color: var(--vscode-foreground);">Filtered to Workspace</span>
                 <button onclick="mapProject()" style="background: transparent; border: none; color: var(--vscode-textLink-foreground); cursor: pointer; padding: 0; font-size: 11px;">Change</button>
               </div>`
            : `<button onclick="mapProject()" style="width: 100%; background: transparent; border: 1px dashed var(--vscode-widget-border); color: var(--vscode-textLink-foreground); padding: 8px; font-size: 12px; border-radius: 4px; cursor: pointer; margin-bottom: 15px; transition: 0.2s;">Link Workspace to Project</button>`;

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
                    <button onclick="refresh()" style="background:transparent; border:none; color:var(--vscode-textLink-foreground); cursor:pointer; padding:0; display:flex; align-items:center;">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 4 23 10 17 10"></polyline><polyline points="1 20 1 14 7 14"></polyline><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path></svg>
                    </button>
                </div>
                
                <button onclick="openPortal()" style="width: 100%; background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; padding: 8px; font-size: 12px; border-radius: 2px; margin-bottom: 15px; cursor: pointer;">Open in Portal</button>
                
                ${projectNote}
                ${taskItems}

                <script>
                    const vscode = acquireVsCodeApi();
                    function updateStatus(taskId, statusVal) {
                        vscode.postMessage({ type: 'updateStatus', taskId: taskId, newStatus: parseInt(statusVal) });
                    }
                    function mapProject() {
                        vscode.postMessage({ type: 'mapProject' });
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
                </script>
            </body>
            </html>
        `;
    }

    private _getHtmlForTaskDetails(task: any, comments: any[], users: any[]) {
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

export function deactivate() { }
