import SwiftUI

struct ContentView: View {
    @StateObject private var network = NetworkManager.shared
    @State private var email = ""
    @State private var password = ""
    @State private var isLoggingIn = false
    @State private var tasks: [TaskItem] = []
    
    var body: some View {
        VStack {
            if network.token != nil {
                TasksListView(tasks: $tasks)
            } else {
                loginView
            }
        }
        .frame(width: 350, height: 450)
        .padding()
    }
    
    private var loginView: some View {
        VStack(spacing: 16) {
            Image(systemName: "checklist")
                .font(.system(size: 48))
                .foregroundColor(.blue)
            
            Text("TaskFlow Companion")
                .font(.headline)
            
            TextField("Email", text: $email)
                .textFieldStyle(RoundedBorderTextFieldStyle())
            
            SecureField("Password", text: $password)
                .textFieldStyle(RoundedBorderTextFieldStyle())
            
            Button(action: performLogin) {
                if isLoggingIn {
                    ProgressView().scaleEffect(0.5)
                } else {
                    Text("Login")
                        .frame(maxWidth: .infinity)
                }
            }
            .buttonStyle(.borderedProminent)
            .disabled(isLoggingIn || email.isEmpty || password.isEmpty)
        }
        .padding()
    }
    
    private func performLogin() {
        isLoggingIn = true
        Task {
            do {
                _ = try await network.login(email: email, password: password)
            } catch {
                print("Login failed: \(error)")
            }
            isLoggingIn = false
        }
    }
}

struct TasksListView: View {
    @Binding var tasks: [TaskItem]
    @State private var selectedTask: TaskItem?
    
    var body: some View {
        VStack {
            HStack {
                Text("My Assignments")
                    .font(.subheadline)
                    .fontWeight(.bold)
                Spacer()
                Button(action: loadTasks) {
                    Image(systemName: "arrow.clockwise")
                }
                Button("Logout") {
                    NetworkManager.shared.logout()
                }
            }
            
            if let task = selectedTask {
                TaskDetailView(task: task, onBack: { selectedTask = nil })
            } else {
                List(tasks) { task in
                    VStack(alignment: .leading, spacing: 6) {
                        Text(task.title).font(.headline)
                        HStack {
                            Text(task.projectName ?? "Independent")
                                .font(.caption)
                                .padding(4)
                                .background(Color.blue.opacity(0.2))
                                .cornerRadius(4)
                            Text(task.priority)
                                .font(.caption)
                                .padding(4)
                                .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.gray, lineWidth: 1))
                        }
                        HStack {
                            Text(task.statusName)
                                .font(.caption)
                                .foregroundColor(.secondary)
                            Spacer()
                            Button("Details") {
                                selectedTask = task
                            }
                            .buttonStyle(.plain)
                            .foregroundColor(.blue)
                        }
                    }
                    .padding(.vertical, 4)
                }
                .listStyle(.plain)
            }
        }
        .onAppear(perform: loadTasks)
    }
    
    private func loadTasks() {
        Task {
            do {
                let fetchedTasks = try await NetworkManager.shared.fetchTasks()
                DispatchQueue.main.async {
                    self.tasks = fetchedTasks
                }
            } catch {
                print("Failed to fetch tasks: \(error)")
            }
        }
    }
}
