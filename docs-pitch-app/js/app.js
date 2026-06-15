/**
 * ApexGov - Main Application Controller
 * Manages tab switching, dark/light themes, dynamic cards rendering,
 * and initializes interactive sub-modules.
 */

import { RoiCalculator } from './roi-calculator.js';
import { HeatmapSimulator } from './heatmap-simulator.js';
import { GateSimulator } from './gate-simulator.js';
import { CodeViewer } from './code-viewer.js';

class App {
    constructor() {
        this.currentTheme = localStorage.getItem('theme') || 'dark';
        this.activeTab = 'pitch';
        
        // Data banks
        this.painPoints = [
            {
                id: 'leakage',
                iconPain: '🔴',
                iconSol: '🟢',
                titlePain: 'Resource Blindspots',
                descPain: 'Over-allocations and developer burnouts go unnoticed, resulting in scheduling slips and talent attrition.',
                titleSol: 'Smart Safety Heatmaps',
                descSol: 'Dynamic heatmaps flag over-allocations, accounting for Bangladeshi weekends and approved leaves.'
            },
            {
                id: 'overrun',
                iconPain: '💸',
                iconSol: '💎',
                titlePain: 'Estimation Overruns',
                descPain: 'High-level project budget allocations drift from bottom-up task consumption, causing margins to leak.',
                titleSol: 'Dual PV vs. EAC Costing',
                descSol: 'Unifies strategic Planned Value (PV) and execution Estimate-at-Completion (EAC) with historical rate integrity.'
            },
            {
                id: 'governance',
                iconPain: '⏳',
                iconSol: '⚡',
                titlePain: 'Governance Friction',
                descPain: 'Checking pipeline criteria and Definition-of-Done (DoD) manually creates massive administrative overhead for PMs.',
                titleSol: 'Automated Gate Inference',
                descSol: 'Heuristically infers validation gates from stage names, blocking invalid transitions with zero manual setup.'
            },
            {
                id: 'delays',
                iconPain: '🐌',
                iconSol: '🚀',
                titlePain: 'Scheduling Bottlenecks',
                descPain: 'Downstream team members idle while waiting for project managers to manually activate tasks after delay blocks.',
                titleSol: 'Background Lag Scheduling',
                descSol: 'An automated background engine automatically activates tasks as soon as scheduled PDM lag times expire.'
            }
        ];

        this.featureLevels = {
            macro: {
                level: 'Macro Level: Portfolio Strategy',
                title: 'Strategic Resource Optimization',
                desc: 'Empowers executives to balance budgets, optimize capacities, and detect financial variances across the entire organization.',
                benefits: [
                    'Unified capacity safety heatmap (Strategic vs. Operational)',
                    'Bangladesh regional weekend tailoring (Friday-Saturday)',
                    'Historical Salary Change & bill rate integration for exact EAC reporting',
                    'Hierarchical capacity mapping (Portfolio -> Epic -> User Story -> Tasks)'
                ]
            },
            meso: {
                level: 'Meso Level: Program Governance',
                title: 'No-Friction Pipeline Gates',
                desc: 'Allows program directors to establish strict delivery standards and quality checks without bogging down developer speed.',
                benefits: [
                    'Automated name-based Stage Gate inference rules',
                    'Definition-of-Done (DoD) verification checklist gates',
                    'Kanban WIP limit transitions and strict flow policies',
                    'Secure role-based access controls (RBAC) via HasPermission decorators'
                ]
            },
            micro: {
                level: 'Micro Level: Team Execution',
                title: 'High-Velocity Task Flow',
                desc: 'Removes daily task administration. Pushes clean, automated workflows directly to individual developer queues.',
                benefits: [
                    'Background Lag Scheduler (Precedence Diagramming Method)',
                    'Automatic status promotion to developer ToDo queues',
                    'RACI-based automatic routing and notification triggers',
                    'System-generated database history changes audit logging'
                ]
            }
        };

        // Cache elements
        this.dom = {
            themeToggle: document.getElementById('btn-theme-toggle'),
            navBtns: document.querySelectorAll('.nav-tabs .tab-btn'),
            tabContents: document.querySelectorAll('.tab-content'),
            painCardsGrid: document.getElementById('pain-cards-grid'),
            btnSwitchPain: document.getElementById('btn-switch-pain'),
            btnSwitchSolution: document.getElementById('btn-switch-solution'),
            detailCards: document.querySelectorAll('.detail-card'),
            
            // Hero buttons
            heroBtnSandbox: document.getElementById('hero-btn-sandbox'),
            heroBtnBlueprint: document.getElementById('hero-btn-blueprint'),
            navCtaBtn: document.getElementById('nav-cta-btn')
        };

        this.init();
    }

    init() {
        // Theme init
        document.documentElement.setAttribute('data-theme', this.currentTheme);
        this.updateThemeIcon();
        this.dom.themeToggle.addEventListener('click', () => this.toggleTheme());

        // Tab routing init
        this.dom.navBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                const tab = btn.getAttribute('data-tab');
                this.switchTab(tab);
            });
        });

        // Hero CTA button routing
        if (this.dom.heroBtnSandbox) {
            this.dom.heroBtnSandbox.addEventListener('click', () => this.switchTab('simulator', 'heatmap'));
        }
        if (this.dom.heroBtnBlueprint) {
            this.dom.heroBtnBlueprint.addEventListener('click', () => this.switchTab('blueprint'));
        }
        if (this.dom.navCtaBtn) {
            this.dom.navCtaBtn.addEventListener('click', (e) => {
                e.preventDefault();
                this.switchTab('simulator', 'roi');
            });
        }

        // Pain switches
        this.dom.btnSwitchPain.addEventListener('click', () => this.renderPainCards(true));
        this.dom.btnSwitchSolution.addEventListener('click', () => this.renderPainCards(false));

        // Details list (Macro-Meso-Micro)
        this.dom.detailCards.forEach(card => {
            card.addEventListener('click', () => {
                this.dom.detailCards.forEach(c => c.classList.remove('active'));
                card.classList.add('active');
                
                const lvl = card.getAttribute('data-level');
                this.renderFeatureLevel(lvl);
            });
        });

        // Sandbox Nav
        this.initSandboxNav();

        // Render initial dynamic states
        this.renderPainCards(true);
        this.renderFeatureLevel('macro');

        // Initialize sub-modules
        this.roi = new RoiCalculator();
        this.heatmap = new HeatmapSimulator();
        this.gates = new GateSimulator();
        this.blueprint = new CodeViewer();
    }

    toggleTheme() {
        this.currentTheme = this.currentTheme === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', this.currentTheme);
        localStorage.setItem('theme', this.currentTheme);
        this.updateThemeIcon();
    }

    updateThemeIcon() {
        const darkIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" viewBox="0 0 24 24"><path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"></path></svg>`;
        const lightIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" viewBox="0 0 24 24"><circle cx="12" cy="12" r="5"></circle><path d="M12 1v2M12 21v2M4.22 4.22l1.42 1.42M18.36 18.36l1.42 1.42M1 12h2M21 12h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42"></path></svg>`;
        this.dom.themeToggle.innerHTML = this.currentTheme === 'dark' ? lightIcon : darkIcon;
    }

    switchTab(tabId, subSimId = null) {
        this.activeTab = tabId;

        // Update Nav button state
        this.dom.navBtns.forEach(btn => {
            const match = btn.getAttribute('data-tab') === tabId;
            btn.classList.toggle('active', match);
            btn.setAttribute('aria-selected', match ? 'true' : 'false');
        });

        // Update Panel display
        this.dom.tabContents.forEach(content => {
            content.classList.toggle('active', content.getAttribute('id') === `tab-${tabId}`);
        });

        // If a sub-simulator panel is targeted (e.g. heatmap)
        if (subSimId) {
            const simBtns = document.querySelectorAll('.sim-nav-btn');
            simBtns.forEach(btn => {
                const match = btn.getAttribute('data-sim') === subSimId;
                btn.classList.toggle('active', match);
            });

            const simPanels = document.querySelectorAll('.sim-panel');
            simPanels.forEach(panel => {
                const match = panel.getAttribute('id') === `sim-panel-${subSimId}`;
                panel.classList.toggle('active', match);
                if (match) panel.style.display = (subSimId === 'heatmap') ? 'grid' : 'flex';
            });
        }

        // Scroll to top of tab container nicely
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }

    renderPainCards(showPain = true) {
        // Toggle active button design
        this.dom.btnSwitchPain.classList.toggle('active', showPain);
        this.dom.btnSwitchSolution.classList.toggle('active', !showPain);
        this.dom.btnSwitchSolution.classList.toggle('solution-mode', !showPain);

        this.dom.painCardsGrid.innerHTML = '';

        this.painPoints.forEach(pt => {
            const card = document.createElement('div');
            card.className = `pain-card ${!showPain ? 'is-solution' : ''}`;
            
            const icon = showPain ? pt.iconPain : pt.iconSol;
            const title = showPain ? pt.titlePain : pt.titleSol;
            const desc = showPain ? pt.descPain : pt.descSol;

            card.innerHTML = `
                <div class="pain-card-icon">${icon}</div>
                <h3 class="pain-card-title">${title}</h3>
                <p class="pain-card-desc">${desc}</p>
            `;
            
            this.dom.painCardsGrid.appendChild(card);
        });
    }

    renderFeatureLevel(levelKey) {
        const data = this.featureLevels[levelKey];
        if (!data) return;

        const levelIndicator = document.getElementById('arch-level-indicator');
        const levelTitle = document.getElementById('arch-level-title');
        const benefitsList = document.getElementById('arch-benefits-list');
        const levelDesc = document.getElementById('arch-level-desc');

        levelIndicator.textContent = data.level;
        levelTitle.textContent = data.title;
        levelDesc.textContent = data.desc;
        
        // Colors corresponding to tags
        if (levelKey === 'macro') {
            levelIndicator.style.color = 'var(--color-primary)';
        } else if (levelKey === 'meso') {
            levelIndicator.style.color = 'var(--color-secondary)';
        } else {
            levelIndicator.style.color = 'var(--color-success)';
        }

        benefitsList.innerHTML = '';
        data.benefits.forEach(benefit => {
            const li = document.createElement('li');
            li.textContent = benefit;
            benefitsList.appendChild(li);
        });
    }

    initSandboxNav() {
        const simBtns = document.querySelectorAll('.sim-nav-btn');
        const simPanels = document.querySelectorAll('.sim-panel');

        simBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                const target = btn.getAttribute('data-sim');

                simBtns.forEach(b => b.classList.remove('active'));
                btn.classList.add('active');

                simPanels.forEach(panel => {
                    const match = panel.getAttribute('id') === `sim-panel-${target}`;
                    panel.classList.toggle('active', match);
                    
                    // Display grid for heatmap, flex for others (matches CSS display defaults)
                    if (match) {
                        panel.style.display = (target === 'heatmap') ? 'grid' : 'flex';
                    } else {
                        panel.style.display = 'none';
                    }
                });
            });
        });
    }
}

// Instantiate on load
window.addEventListener('DOMContentLoaded', () => {
    new App();
});
