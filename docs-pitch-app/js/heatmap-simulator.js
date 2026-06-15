/**
 * ApexGov - Heatmap Simulator Module
 * Models the PMP "Total Load" unified resource capacity algorithm with Bangladesh weekend rules.
 */

export class HeatmapSimulator {
    constructor() {
        this.days = [
            { name: 'Sunday', isWeekend: false, key: 'Sun' },
            { name: 'Monday', isWeekend: false, key: 'Mon' },
            { name: 'Tuesday', isWeekend: false, key: 'Tue' },
            { name: 'Wednesday', isWeekend: false, key: 'Wed' },
            { name: 'Thursday', isWeekend: false, key: 'Thu' },
            { name: 'Friday', isWeekend: true, key: 'Fri' }, // Bangladesh weekend
            { name: 'Saturday', isWeekend: true, key: 'Sat' } // Bangladesh weekend
        ];

        // State variables
        this.allocations = {
            'Project Alpha': [60, 60, 60, 60, 60, 0, 0], // Sun-Thu, Fri-Sat weekend
            'Project Beta':  [0, 50, 50, 0, 0, 0, 0]
        };

        this.tasks = {
            'Project Alpha': [40, 0, 80, 0, 0, 0, 0], // 40% = 3.2 hrs, 80% = 6.4 hrs
            'Project Beta':  [0, 60, 0, 0, 40, 0, 0]
        };

        this.leaves = [false, false, false, false, false, false, false]; // Tuesday leave toggled by user later

        this.dom = {
            tableBody: document.getElementById('heatmap-tbody'),
            toggleLeaveBtn: document.getElementById('btn-toggle-leave'),
            addAllocBtn: document.getElementById('btn-add-alloc'),
            resetBtn: document.getElementById('btn-reset-heat')
        };

        if (this.dom.tableBody) {
            this.init();
        }
    }

    init() {
        this.dom.toggleLeaveBtn.addEventListener('click', () => {
            // Toggle Tuesday (index 2) leave
            this.leaves[2] = !this.leaves[2];
            this.dom.toggleLeaveBtn.textContent = this.leaves[2] ? 'Cancel Tuesday Leave' : 'Approve Tuesday Leave';
            this.dom.toggleLeaveBtn.classList.toggle('btn-primary', !this.leaves[2]);
            this.dom.toggleLeaveBtn.classList.toggle('btn-secondary', this.leaves[2]);
            this.render();
        });

        this.dom.addAllocBtn.addEventListener('click', () => {
            // Add extra Project Beta allocation on Sunday (index 0) to trigger overallocation
            const currentBetaSun = this.allocations['Project Beta'][0];
            if (currentBetaSun === 0) {
                this.allocations['Project Beta'][0] = 50;
                this.dom.addAllocBtn.textContent = 'Reduce Sunday Allocation';
            } else {
                this.allocations['Project Beta'][0] = 0;
                this.dom.addAllocBtn.textContent = 'Increase Sunday Allocation';
            }
            this.render();
        });

        this.dom.resetBtn.addEventListener('click', () => {
            this.allocations['Project Alpha'] = [60, 60, 60, 60, 60, 0, 0];
            this.allocations['Project Beta']  = [0, 50, 50, 0, 0, 0, 0];
            this.tasks['Project Alpha'] = [40, 0, 80, 0, 0, 0, 0];
            this.tasks['Project Beta']  = [0, 60, 0, 0, 40, 0, 0];
            this.leaves = [false, false, false, false, false, false, false];
            
            this.dom.toggleLeaveBtn.textContent = 'Approve Tuesday Leave';
            this.dom.toggleLeaveBtn.className = 'btn btn-primary btn-small';
            this.dom.addAllocBtn.textContent = 'Increase Sunday Allocation';
            
            this.render();
        });

        this.render();
    }

    render() {
        this.dom.tableBody.innerHTML = '';

        // Row 1: Capacity (Hours)
        const capRow = document.createElement('tr');
        capRow.innerHTML = `<td><strong>Daily Capacity</strong></td>`;
        this.days.forEach((day, index) => {
            if (day.isWeekend) {
                capRow.innerHTML += `<td class="weekend-cell">0 hrs <span style="font-size:0.7rem;display:block;">Bangladesh Weekend</span></td>`;
            } else if (this.leaves[index]) {
                capRow.innerHTML += `<td class="weekend-cell" style="color:var(--color-error); font-weight:600;">0 hrs <span style="font-size:0.7rem;display:block;">Approved Leave</span></td>`;
            } else {
                capRow.innerHTML += `<td>8 hrs</td>`;
            }
        });
        this.dom.tableBody.appendChild(capRow);

        // Row 2: Strategic Project Allocation (Strategic)
        const allocRow = document.createElement('tr');
        allocRow.innerHTML = `<td><strong>Strategic Allocations</strong><span style="font-size:0.75rem;color:var(--text-secondary);display:block;">(Project Assigments)</span></td>`;
        this.days.forEach((day, index) => {
            if (day.isWeekend || this.leaves[index]) {
                allocRow.innerHTML += `<td class="weekend-cell">-</td>`;
            } else {
                const totalAlloc = this.calculateStrategicAlloc(index);
                allocRow.innerHTML += `<td>${totalAlloc}%</td>`;
            }
        });
        this.dom.tableBody.appendChild(allocRow);

        // Row 3: Operational Task Demand (Operational)
        const taskRow = document.createElement('tr');
        taskRow.innerHTML = `<td><strong>Task Estimates Demand</strong><span style="font-size:0.75rem;color:var(--text-secondary);display:block;">(Bottom-up active tasks)</span></td>`;
        this.days.forEach((day, index) => {
            if (day.isWeekend || this.leaves[index]) {
                taskRow.innerHTML += `<td class="weekend-cell">-</td>`;
            } else {
                const totalTask = this.calculateTaskDemand(index);
                taskRow.innerHTML += `<td>${totalTask}%</td>`;
            }
        });
        this.dom.tableBody.appendChild(taskRow);

        // Row 4: Combined Total Load (PMP Safety Formula)
        const combinedRow = document.createElement('tr');
        combinedRow.innerHTML = `<td><strong>Unified Load (PMP)</strong><span style="font-size:0.75rem;color:var(--text-secondary);display:block;">(Max(Alloc, Task) per Project)</span></td>`;
        this.days.forEach((day, index) => {
            if (day.isWeekend) {
                combinedRow.innerHTML += `<td class="weekend-cell">0%</td>`;
            } else if (this.leaves[index]) {
                combinedRow.innerHTML += `<td class="weekend-cell" style="color:var(--color-error);">0%</td>`;
            } else {
                const totalLoad = this.calculateCombinedLoad(index);
                let cellClass = 'safe';
                let alertIcon = '';
                
                if (totalLoad > 100) {
                    cellClass = 'danger';
                    alertIcon = '⚠️ ';
                } else if (totalLoad > 85) {
                    cellClass = 'warning';
                }
                
                combinedRow.innerHTML += `
                    <td class="heatmap-cell ${cellClass}">
                        ${alertIcon}${totalLoad}%
                        <span style="font-size:0.7rem;display:block;font-weight:400;color:var(--text-secondary)">
                            ${totalLoad > 100 ? 'Overallocated' : 'Optimal'}
                        </span>
                    </td>`;
            }
        });
        this.dom.tableBody.appendChild(combinedRow);
    }

    calculateStrategicAlloc(dayIndex) {
        let sum = 0;
        for (const proj in this.allocations) {
            sum += this.allocations[proj][dayIndex];
        }
        return sum;
    }

    calculateTaskDemand(dayIndex) {
        let sum = 0;
        for (const proj in this.tasks) {
            sum += this.tasks[proj][dayIndex];
        }
        return sum;
    }

    calculateCombinedLoad(dayIndex) {
        // Safe PMP logic: Max(Allocation, Task Estimate) per project to avoid double counting,
        // then sum the maxima across all projects.
        let totalLoad = 0;
        const projects = Object.keys(this.allocations);
        
        projects.forEach(proj => {
            const alloc = this.allocations[proj][dayIndex] || 0;
            const task = this.tasks[proj][dayIndex] || 0;
            totalLoad += Math.max(alloc, task);
        });

        return totalLoad;
    }
}
