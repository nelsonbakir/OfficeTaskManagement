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
                TabView {
                    TasksListView(tasks: $tasks)
                        .tabItem {
                            Label("Tasks", systemImage: "list.bullet")
                        }
                    
                    CommitHelperView(tasks: tasks)
                        .tabItem {
                            Label("Commit", systemImage: "plus.square.on.square")
                        }
                }
            } else {
                loginView
            }
        }
        .frame(width: 400, height: 500)
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

struct CommitHelperView: View {
    let tasks: [TaskItem]
    @State private var commitMessage: String = ""
    @State private var showSuggestions = false
    @State private var suggestions: [TaskItem] = []
    
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Commit Message Helper")
                .font(.headline)
            
            Text("Type # to mention and link tasks from your current project.")
                .font(.caption)
                .foregroundColor(.secondary)
            
            TextEditor(text: $commitMessage)
                .font(.system(.body, design: .monospaced))
                .padding(4)
                .background(Color(NSColor.textBackgroundColor))
                .cornerRadius(4)
                .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.gray.opacity(0.2), lineWidth: 1))
                .onChange(of: commitMessage) { newValue in
                    checkForMention(newValue)
                }
            
            if showSuggestions {
                VStack(alignment: .leading, spacing: 4) {
                    Text("Select Task (\(suggestions.count) found):")
                        .font(.caption)
                        .fontWeight(.bold)
                    
                    List(suggestions) { task in
                        Button(action: { selectTask(task) }) {
                            VStack(alignment: .leading) {
                                Text("[#\(task.id)] \(task.title)")
                                    .font(.subheadline)
                                    .fontWeight(.bold)
                                Text(task.projectName ?? "Independent")
                                    .font(.caption2)
                                    .foregroundColor(.secondary)
                            }
                            .padding(.vertical, 2)
                        }
                        .buttonStyle(.plain)
                    }
                    .frame(height: 120)
                    .background(Color(NSColor.controlBackgroundColor))
                    .cornerRadius(4)
                    .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.gray.opacity(0.2), lineWidth: 1))
                }
            }
            
            HStack {
                Button(action: copyToClipboard) {
                    Label("Copy message", systemImage: "doc.on.doc")
                }
                .buttonStyle(.borderedProminent)
                .disabled(commitMessage.isEmpty)
                
                Button("Clear") {
                    commitMessage = ""
                    showSuggestions = false
                }
                .buttonStyle(.bordered)
            }
            .padding(.top, 4)
            
            Spacer()
        }
        .padding()
    }
    
    private func checkForMention(_ text: String) {
        if let lastHashIndex = text.lastIndex(of: "#") {
            // Only show suggestions if '#' is at start or preceded by space/newline
            let prefix = text.prefix(upTo: lastHashIndex)
            if prefix.isEmpty || prefix.last?.isWhitespace == true {
                let query = String(text[text.index(after: lastHashIndex)...])
                if query.isEmpty {
                    suggestions = tasks
                    showSuggestions = true
                } else {
                    suggestions = tasks.filter { 
                        "\($0.id)".contains(query) || 
                        $0.title.lowercased().contains(query.lowercased()) 
                    }
                    showSuggestions = !suggestions.isEmpty
                }
            } else {
                showSuggestions = false
            }
        } else {
            showSuggestions = false
        }
    }
    
    private func selectTask(_ task: TaskItem) {
        if let lastHashIndex = commitMessage.lastIndex(of: "#") {
            commitMessage.removeSubrange(lastHashIndex..<commitMessage.endIndex)
            let url = "http://localhost:5035/TaskItems/Details/\(task.id)"
            commitMessage += "[#\(task.id)](\(url)) "
        }
        showSuggestions = false
    }
    
    private func copyToClipboard() {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setString(commitMessage, forType: .string)
    }
}
