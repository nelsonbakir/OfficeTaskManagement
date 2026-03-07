import axios from 'axios';

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
            console.error('Login failed', error);
            throw new Error('Invalid credentials or server unreachable.');
        }
    }

    public async getProjects(): Promise<any[]> {
        const response = await axios.get(`${this.baseUrl}/projectsapi`, { headers: this.getHeaders() });
        return response.data;
    }

    public async getTasks(projectId?: number): Promise<any[]> {
        const url = projectId ? `${this.baseUrl}/tasksapi?projectId=${projectId}` : `${this.baseUrl}/tasksapi`;
        const response = await axios.get(url, { headers: this.getHeaders() });
        return response.data;
    }

    public async updateTaskStatus(taskId: number, newStatus: number): Promise<any> {
        const response = await axios.put(`${this.baseUrl}/tasksapi/${taskId}/status`, { statusId: newStatus }, { headers: this.getHeaders() });
        return response.data;
    }

    public async getNotifications(): Promise<any[]> {
        const response = await axios.get(`${this.baseUrl}/notificationsapi/unread`, { headers: this.getHeaders() });
        return response.data;
    }

    public async markNotificationRead(id: number): Promise<any> {
        const response = await axios.put(`${this.baseUrl}/notificationsapi/${id}/read`, {}, { headers: this.getHeaders() });
        return response.data;
    }

    public async getTaskDetails(taskId: number): Promise<any> {
        const response = await axios.get(`${this.baseUrl}/tasksapi/${taskId}`, { headers: this.getHeaders() });
        return response.data;
    }

    public async getComments(taskId: number): Promise<any[]> {
        const response = await axios.get(`${this.baseUrl}/tasksapi/${taskId}/comments`, { headers: this.getHeaders() });
        return response.data;
    }

    public async postComment(taskId: number, text: string): Promise<any> {
        const response = await axios.post(`${this.baseUrl}/tasksapi/${taskId}/comments`, { text }, { headers: this.getHeaders() });
        return response.data;
    }

    public async getPortalLink(taskId?: number): Promise<string> {
        const returnUrl = encodeURIComponent(taskId ? `/TaskItems/Details/${taskId}` : '/TaskItems');
        const response = await axios.get(`${this.baseUrl}/auth/portal-link?returnUrl=${returnUrl}`, { headers: this.getHeaders() });
        return response.data.url;
    }

    public async getEligibleUsers(): Promise<any[]> {
        const response = await axios.get(`${this.baseUrl}/tasksapi/users`, { headers: this.getHeaders() });
        return response.data;
    }
}
