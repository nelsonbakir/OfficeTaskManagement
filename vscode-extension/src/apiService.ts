import axios, { AxiosError } from 'axios';

export class ApiServiceError extends Error {
    constructor(public readonly code: 'UNAUTHORIZED' | 'NOT_FOUND' | 'NETWORK' | 'UNKNOWN', message: string) {
        super(message);
    }
}

function wrapError(error: unknown): never {
    const err = error as AxiosError;
    if (err.response) {
        if (err.response.status === 401 || err.response.status === 403)
            throw new ApiServiceError('UNAUTHORIZED', 'Token expired or unauthorized.');
        if (err.response.status === 404)
            throw new ApiServiceError('NOT_FOUND', 'Resource not found.');
        throw new ApiServiceError('UNKNOWN', `Server error: ${err.response.status}`);
    }
    if (err.request)
        throw new ApiServiceError('NETWORK', 'API is unreachable.');
    throw new ApiServiceError('UNKNOWN', (error as Error).message);
}

export class ApiService {
    private baseUrl: string = 'http://localhost:5035/api';
    private token: string | null = null;

    constructor() { }

    public setToken(token: string) {
        this.token = token;
    }

    public hasToken(): boolean {
        return !!this.token;
    }

    private getHeaders() {
        return {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${this.token}`,
            'X-Requested-With': 'XMLHttpRequest'
        };
    }

    public async login(email: string, password: string): Promise<any> {
        try {
            const response = await axios.post(`${this.baseUrl}/auth/login`, { email, password });
            return response.data;
        } catch (error) {
            wrapError(error);
        }
    }

    public async getProjects(): Promise<any[]> {
        try {
            const response = await axios.get(`${this.baseUrl}/projectsapi`, { headers: this.getHeaders() });
            return response.data;
        } catch (error) { wrapError(error); }
    }

    public async getTasks(projectId?: number): Promise<any[]> {
        try {
            const hasProjectFilter = projectId !== undefined && projectId !== null && projectId !== 0;
            const url = hasProjectFilter ? `${this.baseUrl}/tasksapi?projectId=${projectId}` : `${this.baseUrl}/tasksapi`;
            const response = await axios.get(url, { headers: this.getHeaders() });
            return response.data;
        } catch (error) { wrapError(error); }
    }

    public async updateTaskStatus(taskId: number, newStatus: number): Promise<any> {
        try {
            const response = await axios.put(`${this.baseUrl}/tasksapi/${taskId}/status`, { statusId: newStatus }, { headers: this.getHeaders() });
            return response.data;
        } catch (error) { wrapError(error); }
    }

    public async getNotifications(): Promise<any[]> {
        try {
            const response = await axios.get(`${this.baseUrl}/notificationsapi/unread`, { headers: this.getHeaders() });
            return response.data;
        } catch (error) { wrapError(error); }
    }

    public async markNotificationRead(id: number): Promise<any> {
        try {
            const response = await axios.put(`${this.baseUrl}/notificationsapi/${id}/read`, {}, { headers: this.getHeaders() });
            return response.data;
        } catch (error) { wrapError(error); }
    }

    public async getTaskDetails(taskId: number): Promise<any> {
        try {
            const response = await axios.get(`${this.baseUrl}/tasksapi/${taskId}`, { headers: this.getHeaders() });
            return response.data;
        } catch (error) { wrapError(error); }
    }

    public async getComments(taskId: number): Promise<any[]> {
        try {
            const response = await axios.get(`${this.baseUrl}/tasksapi/${taskId}/comments`, { headers: this.getHeaders() });
            return response.data;
        } catch (error) { wrapError(error); }
    }

    public async postComment(taskId: number, text: string): Promise<any> {
        try {
            const response = await axios.post(`${this.baseUrl}/tasksapi/${taskId}/comments`, { text }, { headers: this.getHeaders() });
            return response.data;
        } catch (error) { wrapError(error); }
    }

    public async getPortalLink(taskId?: number): Promise<string> {
        try {
            const returnUrl = encodeURIComponent(taskId ? `/TaskItems/Details/${taskId}` : '/TaskItems');
            const response = await axios.get(`${this.baseUrl}/auth/portal-link?returnUrl=${returnUrl}`, { headers: this.getHeaders() });
            return response.data.url;
        } catch (error) { wrapError(error); }
    }

    public async getEligibleUsers(): Promise<any[]> {
        try {
            const response = await axios.get(`${this.baseUrl}/tasksapi/users`, { headers: this.getHeaders() });
            return response.data;
        } catch (error) { wrapError(error); }
    }

    public clearToken() {
        this.token = null;
    }
}
