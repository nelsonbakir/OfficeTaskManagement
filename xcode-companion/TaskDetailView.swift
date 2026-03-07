import SwiftUI

struct TaskDetailView: View {
    let task: TaskItem
    var onBack: () -> Void
    
    @State private var comments: [TaskComment] = []
    @State private var newComment: String = ""
    @State private var isPortalLoading = false
    
    var body: some View {
        VStack(alignment: .leading) {
            HStack {
                Button(action: onBack) {
                    Image(systemName: "chevron.left")
                    Text("Back")
                }
                .buttonStyle(.plain)
                .foregroundColor(.blue)
                Spacer()
            }
            .padding(.bottom, 8)
            
            Text(task.title)
                .font(.headline)
            
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
            .padding(.bottom, 8)
            
            ScrollView {
                VStack(alignment: .leading, spacing: 8) {
                    Text(task.description ?? "No description provided.")
                        .font(.subheadline)
                        .padding()
                        .background(Color(NSColor.windowBackgroundColor))
                        .cornerRadius(8)
                    
                    Button(action: openInPortal) {
                        if isPortalLoading {
                            ProgressView().scaleEffect(0.5)
                        } else {
                            Text("For more detail navigate to the portal")
                                .frame(maxWidth: .infinity)
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .padding(.vertical, 8)
                    
                    Text("DISCUSSIONS")
                        .font(.caption)
                        .fontWeight(.bold)
                        .foregroundColor(.secondary)
                    
                    VStack(spacing: 12) {
                        ForEach(comments) { comment in
                            ChatBubble(comment: comment)
                        }
                    }
                }
            }
            
            HStack {
                TextField("Add a comment...", text: $newComment)
                    .textFieldStyle(RoundedBorderTextFieldStyle())
                
                Button("Post") {
                    postComment()
                }
                .disabled(newComment.isEmpty)
            }
            .padding(.top, 8)
        }
        .onAppear(perform: loadComments)
    }
    
    private func loadComments() {
        Task {
            do {
                let fetched = try await NetworkManager.shared.fetchComments(taskId: task.id)
                DispatchQueue.main.async {
                    self.comments = fetched.reversed() // Oldest first to match bottom-up
                }
            } catch {
                print("Failed to load comments: \(error)")
            }
        }
    }
    
    private func postComment() {
        let textToPost = newComment
        newComment = ""
        Task {
            do {
                try await NetworkManager.shared.postComment(taskId: task.id, text: textToPost)
                loadComments()
            } catch {
                print("Failed to post: \(error)")
            }
        }
    }
    
    private func openInPortal() {
        isPortalLoading = true
        Task {
            do {
                let urlString = try await NetworkManager.shared.getPortalLink(taskId: task.id)
                if let url = URL(string: urlString) {
                    NSWorkspace.shared.open(url)
                }
            } catch {
                print("Failed to get portal link: \(error)")
            }
            DispatchQueue.main.async {
                isPortalLoading = false
            }
        }
    }
}

struct ChatBubble: View {
    let comment: TaskComment
    
    var body: some View {
        VStack(alignment: comment.isSelf ? .trailing : .leading, spacing: 4) {
            Text("\(comment.isSelf ? "You" : comment.authorName) · \(formatDate(comment.createdAt))")
                .font(.system(size: 9))
                .foregroundColor(.secondary)
            
            Text(comment.commentText)
                .font(.system(size: 11))
                .padding(10)
                .background(comment.isSelf ? Color.blue : Color(NSColor.controlBackgroundColor))
                .foregroundColor(comment.isSelf ? .white : .primary)
                .cornerRadius(14)
        }
        .frame(maxWidth: .infinity, alignment: comment.isSelf ? .trailing : .leading)
    }
    
    private func formatDate(_ isoString: String) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = formatter.date(from: isoString) {
            let displayFormatter = DateFormatter()
            displayFormatter.dateStyle = .short
            displayFormatter.timeStyle = .short
            return displayFormatter.string(from: date)
        }
        return isoString
    }
}
