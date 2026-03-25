const API_BASE_URL = 'http://localhost:5035/api';

const ApiService = {
    async getToken() {
        return new Promise((resolve) => {
            chrome.storage.local.get(['token'], (result) => {
                resolve(result.token);
            });
        });
    },

    async setToken(token) {
        return new Promise((resolve) => {
            chrome.storage.local.set({ token }, () => {
                resolve();
            });
        });
    },

    async clearToken() {
        return new Promise((resolve) => {
            chrome.storage.local.remove(['token'], () => {
                resolve();
            });
        });
    },

    async getHeaders() {
        const token = await this.getToken();
        return {
            'Content-Type': 'application/json',
            'X-Requested-With': 'XMLHttpRequest',
            ...(token ? { 'Authorization': `Bearer ${token}` } : {})
        };
    },

    async login(email, password) {
        const response = await fetch(`${API_BASE_URL}/auth/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email, password })
        });
        if (!response.ok) throw new Error('Authentication failed');
        const data = await response.json();
        await this.setToken(data.token);
        return data;
    },

    async getProjects() {
        const response = await fetch(`${API_BASE_URL}/projectsapi`, {
            headers: await this.getHeaders()
        });
        if (!response.ok) throw new Error('Failed to fetch projects');
        return response.json();
    },

    async getTasks(projectId) {
        let url = `${API_BASE_URL}/tasksapi`;
        if (projectId && projectId !== '0') {
            url += `?projectId=${projectId}`;
        }
        const response = await fetch(url, {
            headers: await this.getHeaders()
        });
        if (!response.ok) throw new Error('Failed to fetch tasks');
        return response.json();
    },

    async getTaskDetails(taskId) {
        const response = await fetch(`${API_BASE_URL}/tasksapi/${taskId}`, {
            headers: await this.getHeaders()
        });
        if (!response.ok) throw new Error('Failed to fetch task details');
        return response.json();
    },

    async getComments(taskId) {
        const response = await fetch(`${API_BASE_URL}/tasksapi/${taskId}/comments`, {
            headers: await this.getHeaders()
        });
        if (!response.ok) throw new Error('Failed to fetch comments');
        return response.json();
    },

    async postComment(taskId, text) {
        const response = await fetch(`${API_BASE_URL}/tasksapi/${taskId}/comments`, {
            method: 'POST',
            headers: await this.getHeaders(),
            body: JSON.stringify({ text })
        });
        if (!response.ok) throw new Error('Failed to post comment');
        return response.json();
    },

    async getEligibleUsers() {
        const response = await fetch(`${API_BASE_URL}/tasksapi/users`, {
            headers: await this.getHeaders()
        });
        if (!response.ok) throw new Error('Failed to fetch users');
        return response.json();
    },

    async updateTaskStatus(taskId, statusId) {
        const response = await fetch(`${API_BASE_URL}/tasksapi/${taskId}/status`, {
            method: 'PUT',
            headers: await this.getHeaders(),
            body: JSON.stringify({ statusId })
        });
        if (!response.ok) throw new Error('Failed to update status');
        return response.json();
    }
};
