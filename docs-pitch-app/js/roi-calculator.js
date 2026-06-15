/**
 * ApexGov - ROI Calculator Module
 * Simulates savings from preventing overallocation and automating stage-gate processes.
 */

export class RoiCalculator {
    constructor() {
        this.dom = {
            teamSize: document.getElementById('roi-team-size'),
            teamSizeVal: document.getElementById('roi-team-size-val'),
            salary: document.getElementById('roi-salary'),
            salaryVal: document.getElementById('roi-salary-val'),
            leakage: document.getElementById('roi-leakage'),
            leakageVal: document.getElementById('roi-leakage-val'),
            delays: document.getElementById('roi-delays'),
            delaysVal: document.getElementById('roi-delays-val'),
            
            // Outputs
            hoursSaved: document.getElementById('metric-hours-saved'),
            costAccuracy: document.getElementById('metric-cost-accuracy'),
            totalSavings: document.getElementById('metric-total-savings')
        };

        if (this.dom.teamSize) {
            this.init();
        }
    }

    init() {
        // Attach event listeners
        const inputs = [this.dom.teamSize, this.dom.salary, this.dom.leakage, this.dom.delays];
        inputs.forEach(input => {
            input.addEventListener('input', () => this.calculate());
        });

        // Initial calculation
        this.calculate();
    }

    calculate() {
        // Get raw values
        const teamSize = parseInt(this.dom.teamSize.value, 10);
        const salary = parseInt(this.dom.salary.value, 10);
        const leakagePercent = parseInt(this.dom.leakage.value, 10) / 100;
        const delayWeeks = parseInt(this.dom.delays.value, 10);

        // Update slider values in UI
        this.dom.teamSizeVal.textContent = teamSize;
        this.dom.salaryVal.textContent = `$${(salary / 1000).toFixed(0)}k`;
        this.dom.leakageVal.textContent = `${(leakagePercent * 100).toFixed(0)}%`;
        this.dom.delaysVal.textContent = `${delayWeeks} ${delayWeeks === 1 ? 'Week' : 'Weeks'}`;

        // 1. Calculate payroll and leakage savings
        // Multitasking/over-allocation drops productivity. Smart capacity controls recover 70% of this leakage.
        const totalPayroll = teamSize * salary;
        const leakageRecovered = totalPayroll * leakagePercent * 0.70;

        // 2. Calculate delay/governance savings
        // Automated stage gate inference + background lag scheduling reduces delay blocks.
        // We assume 40 hours per week and recovery of 45% of that bottleneck cost.
        const hourlyRate = salary / 1920; // 1920 working hours per year (48 weeks * 40 hours)
        const bottleneckHoursSaved = teamSize * (delayWeeks * 40) * 0.45;
        const bottleneckCostRecovered = bottleneckHoursSaved * hourlyRate;

        // 3. Total Financial Savings
        const totalSavings = leakageRecovered + bottleneckCostRecovered;

        // 4. Productive Hours Recovered
        // 70% of the leakage hours + 45% of bottleneck delay hours
        const yearlyHoursPerPerson = 1920;
        const totalLeakedHours = teamSize * yearlyHoursPerPerson * leakagePercent;
        const hoursRecovered = (totalLeakedHours * 0.70) + bottleneckHoursSaved;

        // 5. Cost Estimation Accuracy (EAC vs PV correlation)
        // More resources with proper allocation tracking reduces budgeting variance.
        // Base accuracy is 65%. Dynamic tracking adds up to 30% precision based on how big the team is.
        const costAccuracyPct = Math.min(95, 65 + (leakagePercent * 100 * 0.5) + (teamSize > 50 ? 15 : 8));

        // Format and render output metrics
        this.dom.hoursSaved.textContent = Math.round(hoursRecovered).toLocaleString() + ' hrs';
        this.dom.costAccuracy.textContent = Math.round(costAccuracyPct) + '%';
        
        // Currency formating
        this.dom.totalSavings.textContent = new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD',
            maximumFractionDigits: 0
        }).format(totalSavings);
    }
}
