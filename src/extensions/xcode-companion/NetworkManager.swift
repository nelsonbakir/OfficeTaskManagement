import Foundation

class NetworkManager: ObservableObject {
    static let shared = NetworkManager()
    private let baseURL = "http://localhost:5035/api"
    @Published var token: String?
    
    init() {
        if let data = KeychainHelper.standard.read(service: "taskflow", account: "token"),
           let savedToken = String(data: data, encoding: .utf8) {
            self.token = savedToken
        }
    }
    
    private func defaultHeaders() -> [String: String] {
        var headers = ["Content-Type": "application/json", "X-Requested-With": "XMLHttpRequest"]
        if let token = self.token {
            headers["Authorization"] = "Bearer \(token)"
        }
        return headers
    }
    
    func login(email: String, password: String) async throws -> LoginResponse {
        let url = URL(string: "\(baseURL)/auth/login")!
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.allHTTPHeaderFields = defaultHeaders()
        
        let body = ["email": email, "password": password]
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        
        let (data, _) = try await URLSession.shared.data(for: request)
        let response = try JSONDecoder().decode(LoginResponse.self, from: data)
        
        if let tokenData = response.token.data(using: .utf8) {
            KeychainHelper.standard.save(tokenData, service: "taskflow", account: "token")
            DispatchQueue.main.async {
                self.token = response.token
            }
        }
        
        return response
    }
    
    func logout() {
        KeychainHelper.standard.delete(service: "taskflow", account: "token")
        DispatchQueue.main.async {
            self.token = nil
        }
    }
    
    func fetchTasks(projectId: Int? = nil) async throws -> [TaskItem] {
        var urlString = "\(baseURL)/tasksapi"
        if let pid = projectId {
            urlString += "?projectId=\(pid)"
        }
        let url = URL(string: urlString)!
        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        request.allHTTPHeaderFields = defaultHeaders()
        
        let (data, _) = try await URLSession.shared.data(for: request)
        return try JSONDecoder().decode([TaskItem].self, from: data)
    }
    
    func fetchComments(taskId: Int) async throws -> [TaskComment] {
        let url = URL(string: "\(baseURL)/tasksapi/\(taskId)/comments")!
        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        request.allHTTPHeaderFields = defaultHeaders()
        
        let (data, _) = try await URLSession.shared.data(for: request)
        return try JSONDecoder().decode([TaskComment].self, from: data)
    }
    
    func fetchEligibleUsers() async throws -> [EligibleUser] {
        let url = URL(string: "\(baseURL)/tasksapi/users")!
        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        request.allHTTPHeaderFields = defaultHeaders()
        
        let (data, _) = try await URLSession.shared.data(for: request)
        return try JSONDecoder().decode([EligibleUser].self, from: data)
    }
    
    func postComment(taskId: Int, text: String) async throws {
        let url = URL(string: "\(baseURL)/tasksapi/\(taskId)/comments")!
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.allHTTPHeaderFields = defaultHeaders()
        
        let body = ["text": text]
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        
        _ = try await URLSession.shared.data(for: request)
    }
    
    func getPortalLink(taskId: Int?) async throws -> String {
        let returnUrl = taskId != nil ? "/TaskItems/Details/\(taskId!)" : "/TaskItems"
        let encodedReturn = returnUrl.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? ""
        let url = URL(string: "\(baseURL)/auth/portal-link?returnUrl=\(encodedReturn)")!
        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        request.allHTTPHeaderFields = defaultHeaders()
        
        let (data, _) = try await URLSession.shared.data(for: request)
        let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        return json?["url"] as? String ?? ""
    }
    
    func updateTaskStatus(taskId: Int, statusId: Int) async throws {
        let url = URL(string: "\(baseURL)/tasksapi/\(taskId)/status")!
        var request = URLRequest(url: url)
        request.httpMethod = "PUT"
        request.allHTTPHeaderFields = defaultHeaders()
        
        let body = ["statusId": statusId]
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        
        _ = try await URLSession.shared.data(for: request)
    }
}
