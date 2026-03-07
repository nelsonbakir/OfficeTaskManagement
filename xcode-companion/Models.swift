import Foundation

struct LoginResponse: Decodable {
    let token: String
    let expiration: String
    let user: UserData
}

struct UserData: Decodable {
    let id: String
    let fullName: String
    let email: String
    let roles: [String]?
}

struct Project: Identifiable, Decodable, Hashable {
    let id: Int
    let name: String
    let description: String?
}

struct TaskItem: Identifiable, Decodable, Hashable {
    let id: Int
    let title: String
    let description: String?
    let status: Int
    let statusName: String
    let priority: String
    let dueDate: String?
    let projectName: String?
    let featureName: String?
    let estimatedHours: Int?
    let isBacklog: Bool?
}

struct TaskComment: Identifiable, Decodable, Hashable {
    let id: Int
    let commentText: String
    let createdAt: String
    let authorName: String
    let isSelf: Bool
}

struct EligibleUser: Identifiable, Decodable, Hashable {
    let id: String
    let display: String
    let email: String
}
