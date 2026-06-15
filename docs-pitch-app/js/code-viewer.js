/**
 * ApexGov - Code to Concept Viewer Module
 * Displays key architectural C# snippets with C-Level business translations.
 */

export class CodeViewer {
    constructor() {
        this.snippets = {
            resource: {
                fileName: 'ResourceService.cs',
                desc: '<strong>Business Value:</strong> Guarantees local compliance and prevents burnouts. Calculates capacity based on Bangladesh weekends (Friday/Saturday) and approved leaves, while using a unified loading model that prevents double counting between strategic project allocations and active developer tasks.',
                code: `<span class="code-keyword">private async Task</span>&lt;<span class="code-type">int</span>&gt; <span class="code-method">CountWorkingDaysAsync</span>(<span class="code-type">DateTime</span> start, <span class="code-type">DateTime</span> end)
{
    <span class="code-keyword">var</span> holidayRanges = <span class="code-keyword">await</span> _context.PublicHolidays
        .Where(h =&gt; h.ToDate.Date &gt;= start.Date &amp;&amp; h.FromDate.Date &lt;= end.Date)
        .ToListAsync();

    <span class="code-type">int</span> count = <span class="code-keyword">0</span>;
    <span class="code-keyword">for</span> (<span class="code-keyword">var</span> day = start.Date; day &lt;= end.Date; day = day.AddDays(<span class="code-keyword">1</span>))
    {
        <span class="code-comment">// Regional tailoring: Bangladesh Weekends are Friday and Saturday</span>
        <span class="code-keyword">if</span> (day.DayOfWeek != <span class="code-type">DayOfWeek</span>.Friday &amp;&amp; day.DayOfWeek != <span class="code-type">DayOfWeek</span>.Saturday)
        {
            <span class="code-type">bool</span> isHoliday = holidayRanges.Any(h =&gt; h.FromDate.Date &lt;= day &amp;&amp; h.ToDate.Date &gt;= day);
            <span class="code-keyword">if</span> (!isHoliday) count++;
        }
    }
    <span class="code-keyword">return</span> count;
}`
            },
            capacity: {
                fileName: 'CapacityPlanningService.cs',
                desc: '<strong>Business Value:</strong> Budget Leakage Elimination. The heatmap generator calculates strategic vs operational capacity loads and applies a safety envelope formula: <code>Math.Max(AllocPct, TaskPct)</code> per project to ensure we do not double-count overlapping tasks, preventing budget distortion.',
                code: `<span class="code-comment">// Unified PMP Logic: Track Strategic (Alloc), Operational (Task), and Combined (Total)</span>
<span class="code-keyword">var</span> projectStats = <span class="code-keyword">new</span> <span class="code-type">Dictionary</span>&lt;<span class="code-type">int</span>, (<span class="code-type">int</span> AllocPct, <span class="code-type">int</span> TaskPct)&gt;();

<span class="code-comment">// Strategic Allocations</span>
<span class="code-keyword">foreach</span> (<span class="code-keyword">var</span> alloc <span class="code-keyword">in</span> user.ProjectAllocations.Where(a =&gt; a.StartDate &lt;= date &amp;&amp; a.EndDate &gt;= date))
{
    projectStats[alloc.ProjectId] = (alloc.AllocationPercentage, <span class="code-keyword">0</span>);
}

<span class="code-comment">// Operational Tasks</span>
<span class="code-keyword">foreach</span> (<span class="code-keyword">var</span> rt <span class="code-keyword">in</span> userTasks.Where(rt =&gt; rt.StartDate &lt;= date &amp;&amp; rt.EndDate &gt;= date))
{
    <span class="code-keyword">var</span> taskPct = (<span class="code-type">int</span>)<span class="code-type">Math</span>.Round((rt.DailyHours / dailyCap) * <span class="code-keyword">100</span>);
    projectStats[rt.ProjectId] = (projectStats.TryGetValue(rt.ProjectId, <span class="code-keyword">out var</span> ex) 
        ? (ex.AllocPct, ex.TaskPct + taskPct) 
        : (<span class="code-keyword">0</span>, taskPct));
}

<span class="code-comment">// Prevent Double Counting - Apply Safety Envelope</span>
<span class="code-keyword">int</span> strategicTotal = projectStats.Values.Sum(s =&gt; s.AllocPct);
<span class="code-keyword">int</span> operationalTotal = projectStats.Values.Sum(s =&gt; s.TaskPct);
<span class="code-keyword">int</span> combinedTotal = projectStats.Values.Sum(s =&gt; <span class="code-type">Math</span>.Max(s.AllocPct, s.TaskPct));`
            },
            stagegate: {
                fileName: 'StageGateInferenceService.cs',
                desc: '<strong>Business Value:</strong> Zero-Friction Compliance. Instead of requiring Project Managers to manually set up and assign complex Stage Gates and Definition-of-Done (DoD) validations, this heuristic classification engine automatically infers requirements based on the stage name (e.g. testing keywords require test case passes).',
                code: `<span class="code-keyword">public static</span> <span class="code-type">StageGateType</span> <span class="code-method">InferFromName</span>(<span class="code-type">string</span> stageName)
{
    <span class="code-keyword">if</span> (<span class="code-type">string</span>.IsNullOrWhiteSpace(stageName)) <span class="code-keyword">return</span> <span class="code-type">StageGateType</span>.None;
    <span class="code-keyword">var</span> normalized = stageName.Trim().ToLowerInvariant();

    <span class="code-comment">// Priority 1 - Test / QA (requires passed test evidence)</span>
    <span class="code-keyword">if</span> (MatchesAny(normalized, TestPatterns))
        <span class="code-keyword">return</span> <span class="code-type">StageGateType</span>.TestedWithAllCasesPassed;

    <span class="code-comment">// Priority 2 - Review / Audit (requires peer approval comment)</span>
    <span class="code-keyword">if</span> (MatchesAny(normalized, ReviewPatterns))
        <span class="code-keyword">return</span> <span class="code-type">StageGateType</span>.CommittedWithPeerReview;

    <span class="code-comment">// Priority 3 - Build / Development (requires actual hours logged)</span>
    <span class="code-keyword">if</span> (MatchesAny(normalized, ImplementationPatterns))
        <span class="code-keyword">return</span> <span class="code-type">StageGateType</span>.CommittedWithHours;

    <span class="code-keyword">return</span> <span class="code-type">StageGateType</span>.None;
}`
            },
            lagschedule: {
                fileName: 'LagSchedulingService.cs',
                desc: '<strong>Business Value:</strong> Automated Bottleneck Resolution. Runs as a background service to automatically schedule and activate downstream tasks whose Precedence Diagramming Method (PDM) lag delays have elapsed, keeping the pipeline moving without constant manual intervention.',
                code: `<span class="code-keyword">protected override async</span> <span class="code-type">Task</span> <span class="code-method">ExecuteAsync</span>(<span class="code-type">CancellationToken</span> stoppingToken)
{
    <span class="code-keyword">while</span> (!stoppingToken.IsCancellationRequested)
    {
        <span class="code-keyword">await</span> <span class="code-method">ActivateDueStagesAsync</span>(stoppingToken);
        <span class="code-keyword">await</span> <span class="code-type">Task</span>.Delay(_interval, stoppingToken);
    }
}

<span class="code-keyword">private async Task</span> <span class="code-method">ActivateDueStagesAsync</span>(<span class="code-type">CancellationToken</span> ct)
{
    <span class="code-comment">// Find sub-tasks that are "New" but whose PlannedStartDate has elapsed</span>
    <span class="code-keyword">var</span> due = <span class="code-keyword">await</span> db.Tasks
        .Where(t =&gt; t.WorkflowStageId != <span class="code-keyword">null</span> &amp;&amp; t.Status == <span class="code-type">TaskStatus</span>.New &amp;&amp; t.PlannedStartDate &lt;= now &amp;&amp; !t.IsPaused)
        .ToListAsync(ct);

    <span class="code-keyword">foreach</span> (<span class="code-keyword">var</span> stage <span class="code-keyword">in</span> due)
    {
        stage.Status = <span class="code-type">TaskStatus</span>.ToDo; <span class="code-comment">// Auto-promote to Todo queue</span>
        stage.PlannedStartDate = <span class="code-keyword">null</span>;  <span class="code-comment">// Clear so it doesn't loop</span>
        db.TaskHistories.Add(<span class="code-keyword">new</span> <span class="code-type">TaskHistory</span> { ChangeDescription = <span class="code-string">"Lag elapsed - activated automatically."</span> });
    }
    <span class="code-keyword">await</span> db.SaveChangesAsync(ct);
}`
            }
        };

        this.dom = {
            fileName: document.getElementById('code-file-name-el'),
            desc: document.getElementById('code-desc-el'),
            body: document.getElementById('code-body-el'),
            buttons: document.querySelectorAll('.code-menu-btn')
        };

        if (this.dom.fileName) {
            this.init();
        }
    }

    init() {
        this.dom.buttons.forEach(btn => {
            btn.addEventListener('click', () => {
                const target = btn.getAttribute('data-target');
                this.loadSnippet(target);

                // Update active button state
                this.dom.buttons.forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
            });
        });

        // Load resource service by default
        this.loadSnippet('resource');
    }

    loadSnippet(key) {
        const data = this.snippets[key];
        if (data) {
            this.dom.fileName.textContent = data.fileName;
            this.dom.desc.innerHTML = data.desc;
            this.dom.body.innerHTML = `<pre><code>${data.code}</code></pre>`;
        }
    }
}
