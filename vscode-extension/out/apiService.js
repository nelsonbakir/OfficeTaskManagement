"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.ApiService = void 0;
const axios_1 = require("axios");
class ApiService {
    constructor() {
        this.baseUrl = 'http://localhost:5035/api';
        this.token = null;
    }
    setToken(token) {
        this.token = token;
    }
    hasToken() {
        return !!this.token;
    }
    getHeaders() {
        return {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${this.token}`,
            'X-Requested-With': 'XMLHttpRequest'
        };
    }
    async login(email, password) {
        try {
            const response = await axios_1.default.post(`${this.baseUrl}/auth/login`, { email, password });
            return response.data;
        }
        catch (error) {
            console.error('Login failed', error);
            throw new Error('Invalid credentials or server unreachable.');
        }
    }
    async getProjects() {
        const response = await axios_1.default.get(`${this.baseUrl}/projectsapi`, { headers: this.getHeaders() });
        return response.data;
    }
    async getTasks(projectId) {
        const url = projectId ? `${this.baseUrl}/tasksapi?projectId=${projectId}` : `${this.baseUrl}/tasksapi`;
        const response = await axios_1.default.get(url, { headers: this.getHeaders() });
        return response.data;
    }
    async updateTaskStatus(taskId, newStatus) {
        const response = await axios_1.default.put(`${this.baseUrl}/tasksapi/${taskId}/status`, { statusId: newStatus }, { headers: this.getHeaders() });
        return response.data;
    }
    async getNotifications() {
        const response = await axios_1.default.get(`${this.baseUrl}/notificationsapi/unread`, { headers: this.getHeaders() });
        return response.data;
    }
    async markNotificationRead(id) {
        const response = await axios_1.default.put(`${this.baseUrl}/notificationsapi/${id}/read`, {}, { headers: this.getHeaders() });
        return response.data;
    }
    async getTaskDetails(taskId) {
        const response = await axios_1.default.get(`${this.baseUrl}/tasksapi/${taskId}`, { headers: this.getHeaders() });
        return response.data;
    }
    async getComments(taskId) {
        const response = await axios_1.default.get(`${this.baseUrl}/tasksapi/${taskId}/comments`, { headers: this.getHeaders() });
        return response.data;
    }
    async postComment(taskId, text) {
        const response = await axios_1.default.post(`${this.baseUrl}/tasksapi/${taskId}/comments`, { text }, { headers: this.getHeaders() });
        return response.data;
    }
    async getPortalLink(taskId) {
        const returnUrl = encodeURIComponent(taskId ? `/TaskItems/Details/${taskId}` : '/TaskItems');
        const response = await axios_1.default.get(`${this.baseUrl}/auth/portal-link?returnUrl=${returnUrl}`, { headers: this.getHeaders() });
        return response.data.url;
    }
    async getEligibleUsers() {
        const response = await axios_1.default.get(`${this.baseUrl}/tasksapi/users`, { headers: this.getHeaders() });
        return response.data;
    }
}
exports.ApiService = ApiService;
//# sourceMappingURL=apiService.js.map