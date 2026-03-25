"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.ApiService = exports.ApiServiceError = void 0;
const axios_1 = require("axios");
class ApiServiceError extends Error {
    constructor(code, message) {
        super(message);
        this.code = code;
    }
}
exports.ApiServiceError = ApiServiceError;
function wrapError(error) {
    const err = error;
    if (err.response) {
        if (err.response.status === 401 || err.response.status === 403)
            throw new ApiServiceError('UNAUTHORIZED', 'Token expired or unauthorized.');
        if (err.response.status === 404)
            throw new ApiServiceError('NOT_FOUND', 'Resource not found.');
        throw new ApiServiceError('UNKNOWN', `Server error: ${err.response.status}`);
    }
    if (err.request)
        throw new ApiServiceError('NETWORK', 'API is unreachable.');
    throw new ApiServiceError('UNKNOWN', error.message);
}
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
            wrapError(error);
        }
    }
    async getProjects() {
        try {
            const response = await axios_1.default.get(`${this.baseUrl}/projectsapi`, { headers: this.getHeaders() });
            return response.data;
        }
        catch (error) {
            wrapError(error);
        }
    }
    async getTasks(projectId) {
        try {
            const hasProjectFilter = projectId !== undefined && projectId !== null && projectId !== 0;
            const url = hasProjectFilter ? `${this.baseUrl}/tasksapi?projectId=${projectId}` : `${this.baseUrl}/tasksapi`;
            const response = await axios_1.default.get(url, { headers: this.getHeaders() });
            return response.data;
        }
        catch (error) {
            wrapError(error);
        }
    }
    async updateTaskStatus(taskId, newStatus) {
        try {
            const response = await axios_1.default.put(`${this.baseUrl}/tasksapi/${taskId}/status`, { statusId: newStatus }, { headers: this.getHeaders() });
            return response.data;
        }
        catch (error) {
            wrapError(error);
        }
    }
    async getNotifications() {
        try {
            const response = await axios_1.default.get(`${this.baseUrl}/notificationsapi/unread`, { headers: this.getHeaders() });
            return response.data;
        }
        catch (error) {
            wrapError(error);
        }
    }
    async markNotificationRead(id) {
        try {
            const response = await axios_1.default.put(`${this.baseUrl}/notificationsapi/${id}/read`, {}, { headers: this.getHeaders() });
            return response.data;
        }
        catch (error) {
            wrapError(error);
        }
    }
    async getTaskDetails(taskId) {
        try {
            const response = await axios_1.default.get(`${this.baseUrl}/tasksapi/${taskId}`, { headers: this.getHeaders() });
            return response.data;
        }
        catch (error) {
            wrapError(error);
        }
    }
    async getComments(taskId) {
        try {
            const response = await axios_1.default.get(`${this.baseUrl}/tasksapi/${taskId}/comments`, { headers: this.getHeaders() });
            return response.data;
        }
        catch (error) {
            wrapError(error);
        }
    }
    async postComment(taskId, text) {
        try {
            const response = await axios_1.default.post(`${this.baseUrl}/tasksapi/${taskId}/comments`, { text }, { headers: this.getHeaders() });
            return response.data;
        }
        catch (error) {
            wrapError(error);
        }
    }
    async getPortalLink(taskId) {
        try {
            const returnUrl = encodeURIComponent(taskId ? `/TaskItems/Details/${taskId}` : '/TaskItems');
            const response = await axios_1.default.get(`${this.baseUrl}/auth/portal-link?returnUrl=${returnUrl}`, { headers: this.getHeaders() });
            return response.data.url;
        }
        catch (error) {
            wrapError(error);
        }
    }
    async getEligibleUsers() {
        try {
            const response = await axios_1.default.get(`${this.baseUrl}/tasksapi/users`, { headers: this.getHeaders() });
            return response.data;
        }
        catch (error) {
            wrapError(error);
        }
    }
    clearToken() {
        this.token = null;
    }
}
exports.ApiService = ApiService;
//# sourceMappingURL=apiService.js.map