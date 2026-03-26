const API_BASE_URL = 'http://localhost:5035/api';
let tasksCache = [];
let lastProjectId = -2;

async function getToken() {
    const result = await chrome.storage.local.get(['token']);
    return result.token;
}

async function getHeaders() {
    const token = await getToken();
    return {
        'Content-Type': 'application/json',
        'X-Requested-With': 'XMLHttpRequest',
        ...(token ? { 'Authorization': `Bearer ${token}` } : {})
    };
}

async function fetchTasks() {
    const token = await getToken();
    if (!token) return [];

    const result = await chrome.storage.local.get(['selectedProjectId']);
    const projectId = result.selectedProjectId;

    let url = `${API_BASE_URL}/tasksapi`;
    if (projectId && projectId !== '0') {
        url += `?projectId=${projectId}`;
    }

    try {
        const response = await fetch(url, { headers: await getHeaders() });
        if (response.ok) {
            tasksCache = await response.json();
            lastProjectId = projectId;
        }
    } catch (e) {
        console.error('TaskFlow: Background fetch failed', e);
    }
}

// Listen for messages
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.type === 'getTasks') {
        sendResponse({ tasks: tasksCache });
    } else if (request.type === 'updateCache') {
        fetchTasks().then(() => sendResponse({ success: true }));
        return true; // Keep channel open for async
    }
});

// Initial fetch on installed or startup
chrome.runtime.onInstalled.addListener(() => {
    fetchTasks();
});

chrome.runtime.onStartup.addListener(() => {
    fetchTasks();
});

// Periodic refresh (every 5 minutes)
chrome.alarms.create('refreshTasks', { periodInMinutes: 5 });
chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === 'refreshTasks') {
        fetchTasks().catch(console.error);
    }
});
