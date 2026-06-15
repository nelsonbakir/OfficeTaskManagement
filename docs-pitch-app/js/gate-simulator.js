/**
 * ApexGov - Stage Gate Inference Simulator
 * Ports the C# pattern-matching engine from StageGateInferenceService.cs to demo automated DoD governance.
 */

export class GateSimulator {
    constructor() {
        // Pattern lists matching C# codebase exactly
        this.patterns = {
            test: [
                "qa", "quality assurance", "test", "testing", "tester",
                "uat", "user acceptance", "acceptance test", "acceptance testing",
                "regression", "regression test", "smoke test", "smoke testing",
                "integration test", "e2e test", "end-to-end test", "verify", "verification"
            ],
            review: [
                "review", "peer review", "code review", "audit", "security audit",
                "approval", "approve", "inspect", "inspection", "validate", "validation",
                "sign off", "signoff", "sign-off", "walkthrough", "desk check",
                "assessment", "evaluation"
            ],
            implementation: [
                "develop", "development", "developer", "implement", "implementation",
                "build", "building", "code", "coding", "fix", "bug fix", "hotfix",
                "refactor", "refactoring", "migrate", "migration", "data migration",
                "setup", "set up", "set-up", "configure", "configuration", "install",
                "installation", "design", "ui design", "ux design", "prototype",
                "prototyping", "deploy", "deployment", "release", "release candidate",
                "integration", "integrating"
            ],
            completion: [
                "done", "complete", "completion", "document", "documentation",
                "write docs", "update docs", "handover", "hand over", "hand-over",
                "closure", "close", "closing", "publish", "publishing", "announce",
                "announcement", "training", "knowledge transfer", "demo", "demonstration",
                "stage", "staging"
            ]
        };

        this.dom = {
            input: document.getElementById('gate-stage-input'),
            badge: document.getElementById('gate-badge-el'),
            description: document.getElementById('gate-desc-el'),
            checklist: document.getElementById('gate-checklist-el'),
            presets: document.querySelectorAll('.preset-chip')
        };

        if (this.dom.input) {
            this.init();
        }
    }

    init() {
        // Input listener
        this.dom.input.addEventListener('input', () => {
            this.evaluate(this.dom.input.value);
        });

        // Preset chip listeners
        this.dom.presets.forEach(chip => {
            chip.addEventListener('click', () => {
                const val = chip.getAttribute('data-value');
                this.dom.input.value = val;
                this.evaluate(val);
            });
        });

        // Initial evaluation with default value
        this.evaluate(this.dom.input.value || "Code Review");
    }

    evaluate(stageName) {
        if (!stageName || stageName.trim() === '') {
            this.renderGate('None');
            return;
        }

        const normalized = stageName.trim().toLowerCase();

        // Priority 1: Test / QA
        if (this.matchesAny(normalized, this.patterns.test)) {
            this.renderGate('TestedWithAllCasesPassed');
            return;
        }

        // Priority 2: Review / Audit
        if (this.matchesAny(normalized, this.patterns.review)) {
            this.renderGate('CommittedWithPeerReview');
            return;
        }

        // Priority 3: Implementation / Build / Deploy
        if (this.matchesAny(normalized, this.patterns.implementation)) {
            this.renderGate('CommittedWithHours');
            return;
        }

        // Priority 4: Completion / Handover
        if (this.matchesAny(normalized, this.patterns.completion)) {
            this.renderGate('CommittedOnly');
            return;
        }

        // Priority 5: None
        this.renderGate('None');
    }

    matchesAny(input, patterns) {
        return patterns.some(pattern => input.includes(pattern));
    }

    renderGate(gateType) {
        // Clear checklist
        this.dom.checklist.innerHTML = '';
        
        let badgeText = '';
        let badgeClass = '';
        let descText = '';
        let checklistItems = [];

        switch (gateType) {
            case 'TestedWithAllCasesPassed':
                badgeText = 'Tested with Cases Passed';
                badgeClass = 'tested';
                descText = 'Requires complete test execution quality evidence. The task cannot transition to Done until tests are logged and passed.';
                checklistItems = [
                    { text: 'Set sub-task status to [Tested]', checked: true },
                    { text: 'Ensure parent User Story has at least 1 linked TestCase', checked: true },
                    { text: 'Verify all linked TestCases are marked [Passed] (automated verification)', checked: false },
                    { text: 'System records verification check in DB audit log', checked: false }
                ];
                break;

            case 'CommittedWithPeerReview':
                badgeText = 'Peer Reviewed';
                badgeClass = 'review';
                descText = 'Requires collaborative confirmation. A second opinion must be written to eliminate lone developer defects.';
                checklistItems = [
                    { text: 'Set sub-task status to [Committed]', checked: true },
                    { text: 'Log at least 1 review comment on the sub-task discussion board', checked: false },
                    { text: 'Record the reviewer user ID in audit ledger', checked: false }
                ];
                break;

            case 'CommittedWithHours':
                badgeText = 'Committed with Hours';
                badgeClass = 'hours';
                descText = 'Requires actual resource effort transparency. Hours logged must be recorded to validate budget consumption.';
                checklistItems = [
                    { text: 'Set sub-task status to [Committed]', checked: true },
                    { text: 'Log actual working hours (> 0 hours recorded in timesheet)', checked: false },
                    { text: 'Update remaining estimate hours', checked: false }
                ];
                break;

            case 'CommittedOnly':
                badgeText = 'Committed Only';
                badgeClass = 'commit';
                descText = 'Lightweight governance. Transitions are open once completion intent is signaled.';
                checklistItems = [
                    { text: 'Set sub-task status to [Committed]', checked: false },
                    { text: 'Signal completion milestone to dependent stages', checked: false }
                ];
                break;

            case 'None':
            default:
                badgeText = 'No Gate Enforced';
                badgeClass = 'none';
                descText = 'Stage passes freely. Ideal for initial brainstorming, documentation notes, or meetings with zero technical output.';
                checklistItems = [
                    { text: 'No checklist requirements. Move stage freely.', checked: true }
                ];
                break;
        }

        // Render Badge and description
        this.dom.badge.textContent = badgeText;
        this.dom.badge.className = `gate-badge ${badgeClass}`;
        this.dom.description.textContent = descText;

        // Render checklist items
        checklistItems.forEach(item => {
            const li = document.createElement('li');
            li.className = `checklist-item ${item.checked ? 'checked' : ''}`;
            li.innerHTML = `
                <div class="checklist-checkbox">
                    ${item.checked ? '✓' : ''}
                </div>
                <span class="checklist-text">${item.text}</span>
            `;
            this.dom.checklist.appendChild(li);
        });
    }
}
