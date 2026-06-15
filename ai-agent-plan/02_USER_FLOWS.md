# User Flows — AI Assistance at Every Entry Point
**OfficeTaskManagement · All Entities · Create + Edit + Re-estimate**

> This document answers: "How does a user interact with AI at each step?"  
> Every flow is user-centric first, technical second.

---

## Design Principles for AI UX

1. **Never block the form** — AI assistance is always additive. User can ignore the panel and save normally.
2. **Trigger on intent, not load** — The AI panel activates when the user has typed a meaningful title (≥10 chars) and pauses, OR on explicit button click. Never on page load.
3. **Show reasoning, not just answers** — Every estimate shows a `Rationale` sentence so the PM can judge quality.
4. **Context-driven depth** — "Step-by-step" generates one level of children; "Full Breakdown" generates the full sub-tree. User chooses.
5. **Always editable** — AI-suggested values populate form fields; user can modify before saving.
6. **Re-estimation is a first-class action** — The "Re-estimate with AI" button appears on Edit forms for all entities.

---

## FLOW 1: Creating a New Epic

### Trigger Point
`GET /Epics/Create?projectId=X` — user is on the Create Epic form.

### Step-by-step Journey

```
Step 1: User selects a Project from the dropdown
         → JS fires: "project selected" event
         → AI panel becomes visible (not yet active, shows placeholder)

Step 2: User types Epic name, e.g. "User Authentication System"
         → After 500ms debounce (or on blur)
         → [Analyze with AI ▶] button glows, becomes clickable

Step 3: User clicks [Analyze with AI ▶]
         → Spinner shows on panel (non-blocking — form still usable)
         → JS calls: POST /api/ai/estimate
           { entityType: "Epic", projectId: 5, title: "User Auth System", description: "..." }
         → ContextBuilderService loads:
             · Project name + description
             · Existing epics (name + description only, no deep tree) — COMPRESSED
             · Last 6 sprints velocity stats (avg story points/sprint) — SINGLE NUMBER
             · Top 3 similar epics from history (PERT actual vs estimated) — 3 ROWS MAX
             · Code context: top-3 code chunks related to "authentication" — IF RAG READY

Step 4: Gemini returns EstimationResult (JSON)
         → Panel populates:
           ○ Description suggestion (auto-fills textarea if user left it blank)
           ○ Rationale: "Based on 3 similar auth epics in your project (avg 240h actual)"
           ○ Estimated total hours: 210h
           ○ Risks: ["LDAP library compatibility", "SSO scope creep"]

         AI panel also shows: 
         ┌──────────────────────────────────────────────────────────┐
         │  ✨ AI Estimation                                        │
         │  Rationale: Based on 3 auth-related epics in HR System  │
         │  (avg 240h actual vs 180h estimated, 33% overrun)        │
         │                                                          │
         │  Suggested child Features (4):                          │
         │  ☑ Login & Registration (UI + Backend)       ~40h       │
         │  ☑ Password Reset & MFA                      ~24h       │
         │  ☑ LDAP / SSO Integration                    ~80h       │
         │  ☑ Role-Based Authorization                   ~32h       │
         │                                                          │
         │  [Create Epic Only]  [Create Epic + Selected Features]  │
         └──────────────────────────────────────────────────────────┘

Step 5a: User clicks [Create Epic Only]
          → Normal form POST to /Epics/Create
          → Redirects to Epic details page

Step 5b: User clicks [Create Epic + Selected Features ▶]
          → JS calls: POST /api/ai/bulk-create
            { epicId: <new>, items: [{type:"Feature", title:"Login & Reg", ...}, ...] }
          → Server creates Epic, then creates all checked Features in one transaction
          → Redirects to Epic Details showing the 4 new Features
          → Each Feature card shows "AI Generated" badge

Step 6 (Optional — on Epic detail page):
         User clicks on a Feature → "Expand with AI" button
         → Same flow repeats for that Feature → generates UserStories
```

---

## FLOW 2: Creating a New Feature (under an Epic)

### Trigger Point
`GET /Features/Create?epicId=X` — context already has Epic scope.

### Key Differences from Epic Flow
- AI immediately knows the Epic context (no project selection needed)
- Suggests **UserStories** as children (not Features)
- Pulls existing Features of this Epic to avoid duplication

```
Context packet sent to Gemini:
  · Project name
  · Epic name + description
  · Existing features of this epic (names only, de-dup awareness)
  · Feature title user typed
  · Code context: top-3 chunks related to feature title keyword

AI returns:
  · Estimated hours (Feature level — typically 8–80h range)
  · Priority suggestion
  · Acceptance criteria draft (for the feature)
  · 2–6 suggested UserStory titles with brief descriptions
  · Rationale + risk notes
```

---

## FLOW 3: Creating a New UserStory (under a Feature)

### Trigger Point
`GET /UserStories/Create?featureId=X`

### Key AI Additions
- **Acceptance Criteria generation** is the primary value here (most time-saving)
- Suggests **Tasks** as children (can be step-by-step or full cascade)
- PERT estimation at story level: O/M/P hours fields are auto-populated

```
AI panel sections:
  1. Description auto-fill (if blank)
  2. Acceptance Criteria (markdown, full draft) → pastes into AcceptanceCriteria field
  3. PERT: Optimistic 4h / Most Likely 8h / Pessimistic 16h → fills O/M/P fields
  4. Suggested Tasks (3–8):
     ☑ API endpoint implementation       O:2h M:4h P:8h
     ☑ Unit tests                        O:1h M:2h P:4h
     ☑ Front-end component               O:2h M:5h P:10h
     ☑ Integration test                  O:1h M:2h P:4h
  5. [Create Story Only] | [Create Story + Selected Tasks]
```

---

## FLOW 4: Creating a New Task (standalone or under UserStory)

### Trigger Point
`GET /TaskItems/Create` or `/TaskItems/Create?userStoryId=X`

### AI Value Additions
- PERT O/M/P hours auto-fill (most impactful — currently a manual burden)
- Workflow template suggestion ("This looks like a backend implementation — recommend 'Dev → Review → QA' template")
- Sprint suggestion ("Based on current sprint load, recommend Sprint 7 — currently at 65% capacity")
- Resource suggestion ("Available resources with matching skills: John Doe (68% available)")

```
AI panel:
  ○ O/M/P hour estimates → fills form fields directly
  ○ Priority: High
  ○ Suggested Workflow Template: "Standard Dev Pipeline"
  ○ Sprint: Sprint 7 (65% loaded, 35% headroom = 28h available)
  ○ Rationale: "Similar backend auth tasks averaged 14h actual in last 3 sprints"

  [Apply Estimates] → fills all PERT fields + priority
```

---

## FLOW 5: Creating a New Project

### Trigger Point
`GET /Projects/Create`

### AI Value — Strategic Level
- Suggests Epic breakdown (3–8 major epics) from project description
- Estimates total project budget in BDT
- Suggests project timeline (start week, milestones)
- Identifies required skills → auto-populates RequiredSkills field

```
AI panel:
  Project "HR Management System — full rebuild"
  
  Suggested Epics (6):
  ☑ User & Access Management          Est: 240h  ~৳192,000
  ☑ Leave & Attendance                Est: 180h  ~৳144,000
  ☑ Payroll Processing                Est: 320h  ~৳256,000
  ☑ Performance Appraisal            Est: 160h  ~৳128,000
  ☑ Reports & Analytics              Est: 120h  ~৳96,000
  ☑ Admin & Configuration            Est: 80h   ~৳64,000
  ─────────────────────────────────────────────
  Total PERT Estimate:               ~1,100h  ~৳880,000

  Required Skills: C#, ASP.NET, PostgreSQL, React
  [Apply to Project] | [Create Project + All Epics]
```

---

## FLOW 6: Re-estimation on Existing Items

### Trigger Point
`GET /Epics/Edit/5` (or any Edit page) — user sees existing item.

### When to Re-estimate
- Task has been in-progress for >X days (AI proactively suggests "re-estimate?")
- User clicks explicit [Re-estimate with AI ▶] button
- After scope change (description edit triggers "scope may have changed" notice)

### AI Re-estimation Behavior
- Reads **actual hours logged so far** from TaskHistory
- Compares against original estimate
- Recalculates remaining effort
- Presents a revised O/M/P with explanation of why

```
Re-estimation panel on Edit page:
  ○ Original estimate: 40h
  ○ Actual so far: 28h (logged)
  ○ AI revised remaining: 18–25h (scope drift detected from comments)
  ○ New PERT total: 46–53h
  ○ Status: ⚠ At Risk (25% over budget)
  ○ Rationale: "3 comment threads indicate scope additions. 
    Recommend formal change request."
  [Apply Revised Estimate] | [Dismiss]
```

---

## FLOW 7: Multi-turn Copilot Sidebar (Phase 4)

### Trigger Point
Floating button "🤖 AI Copilot" on any page — opens persistent sidebar.

### Example Dialogue

```
PM: "Plan the authentication epic for project HR System"

AI: I found your 'HR System' project. It has 4 existing epics, none covering 
    authentication. Based on your codebase (I found IdentityService.cs, 
    UserController.cs — 2 relevant files) and your team's velocity of 
    42 story points/sprint, here's my recommendation:

    📦 Epic: Authentication & Access Management

    Features (4):
    ├─ Login / Registration     ~40h PERT   ← Similar to 'Portal Login' in HIS project
    ├─ Password Reset & MFA     ~24h PERT
    ├─ LDAP Integration         ~80h PERT   ← Your codebase has no LDAP; spike needed
    └─ Role-Based Auth          ~32h PERT

    Total: ~176h · BDT ৳140,800 · 4-5 sprints

    Shall I create all of these, or adjust first?

PM: "Skip LDAP for now, use JWT only like we already have"

AI: Understood — removing LDAP Feature. JWT is already implemented in your 
    codebase (JwtBearerExtension.cs, line 45). I'll reference that pattern.
    
    Revised: 3 Features · ~96h · BDT ৳76,800 · 2-3 sprints

    [✓ Create Epic + 3 Features]  [✎ Edit Plan]
```

---

## Entity × AI Feature Matrix

| Entity | Estimation | Child Suggestions | Acceptance Criteria | Re-estimate | Copilot |
|--------|-----------|-------------------|--------------------| -------------|---------|
| Project | ✅ Budget + Timeline | ✅ Epics (3-8) | — | ✅ | ✅ |
| Epic | ✅ PERT hours | ✅ Features (2-8) | — | ✅ | ✅ |
| Feature | ✅ PERT hours | ✅ UserStories (2-8) | — | ✅ | ✅ |
| UserStory | ✅ PERT O/M/P | ✅ Tasks (3-10) | ✅ Full draft | ✅ | ✅ |
| TaskItem | ✅ PERT O/M/P | — | ✅ DoD draft | ✅ | ✅ |
