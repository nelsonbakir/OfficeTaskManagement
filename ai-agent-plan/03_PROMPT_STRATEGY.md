# Prompt Engineering Strategy — Token-Efficient, Accurate, Context-Driven
**OfficeTaskManagement · Gemini AI Integration**

---

## The Core Problem: Accuracy vs Token Cost

Naive prompting = dump the entire DB into the prompt → expensive, slow, often worse.  
Smart prompting = **context budget** — inject only what Gemini needs, in compressed form.

**Target budget per estimation call**: ≤ 8,000 tokens total (input + output)  
**Target latency**: < 3 seconds for estimation, < 8 seconds for child suggestions

---

## Context Budget Allocation

| Context Section | Max Tokens | What It Contains |
|----------------|-----------|-----------------|
| System prompt (static) | ~500 | Role, rules, output format |
| Entity being estimated | ~300 | Title + description (truncated to 500 chars) |
| Parent context | ~400 | Parent entity name + description (1 level up) |
| Sibling context | ~600 | Names of existing siblings (no descriptions) |
| Historical data | ~500 | Last 5 similar items: title, estimated_h, actual_h (CSV format) |
| Code context (RAG) | ~1,500 | Top-3 code chunks (only after Phase 3 RAG is ready) |
| Output schema | ~200 | JSON schema definition |
| **Total input budget** | **~4,000** | |
| **Output budget** | **~4,000** | Estimation + rationale + child suggestions |

---

## System Prompt (Static — Same for All Calls)

```
You are an expert PMP-certified project manager and software architect assistant 
for a Bangladesh-based software team. You produce structured JSON estimates.

DOMAIN RULES:
- Currency: BDT only. Hourly rate provided in context.
- Working days: Sunday–Thursday (Bangladesh). Friday+Saturday = weekend.
- Estimation method: PERT three-point (O + 4M + P) / 6.
- Hierarchy: Project → Epic → Feature → UserStory → TaskItem.
- Priority values: Low | Medium | High | Critical.
- Story points: Fibonacci only (1,2,3,5,8,13,21).

OUTPUT RULES:
- Always return valid JSON matching the provided schema.
- Rationale must be ≤ 2 sentences referencing actual historical data from context.
- Never invent data not present in the context.
- If context is insufficient, return conservative estimates with low confidence.
- Child item titles must be unique from existing siblings listed in context.
```

---

## Context Compression Rules (ContextBuilderService)

### Rule 1: Siblings — Names Only, No Descriptions
```
BAD (wastes 2,000 tokens):
"Existing epics: [{id:1, name:'Login', description:'This epic covers the full login...', features:[...]}, ...]"

GOOD (uses 120 tokens):
"Existing epics in this project: Login, Leave Management, Payroll, Reporting"
```

### Rule 2: History — Aggregated Stats, Not Raw Rows
```
BAD (wastes 3,000 tokens):
"Past tasks: [{id:123, title:'...' estimatedHours:8, actualHours:11, comments:[...]}, ...]"

GOOD (uses 200 tokens):
"Historical estimation accuracy for this project (last 12 months):
- Backend tasks: avg 8h estimated → 11h actual (38% overrun)
- Frontend tasks: avg 6h estimated → 7h actual (17% overrun)
- Integration tasks: avg 12h estimated → 18h actual (50% overrun)
- Most similar past item: 'JWT Auth Implementation' → estimated 16h, actual 22h"
```

### Rule 3: Code Context — Chunk Summaries, Not Full Files
```
BAD: Dump entire IdentityService.cs (800 lines = ~6,000 tokens)

GOOD (uses ~500 tokens per chunk):
"[Codebase excerpt — Services/IdentityService.cs:45-89]
JWT token generation and validation. Uses HS256. 
Key methods: GenerateToken(user), ValidateToken(token), RefreshToken(token).
Dependencies: Microsoft.IdentityModel.Tokens, appsettings Jwt:Key"
```

### Rule 4: Truncate Long Descriptions
```csharp
// In ContextBuilderService:
private static string Truncate(string? text, int maxChars = 400)
    => string.IsNullOrEmpty(text) ? ""
     : text.Length <= maxChars ? text
     : text[..maxChars] + "...";
```

### Rule 5: Use Response Schema for Structured Output
Always use `response_mime_type: "application/json"` + `response_schema` to:
- Eliminate unparseable text responses
- Guarantee field presence
- Reduce output token waste (no explanatory prose in JSON)

---

## Prompt Templates

### Template A: Entity Estimation (Epic/Feature/UserStory/Task)

```json
{
  "systemInstruction": { "parts": [{ "text": "<<STATIC_SYSTEM_PROMPT>>" }] },
  "contents": [{
    "parts": [{
      "text": "ESTIMATION REQUEST\n\nEntity Type: {entityType}\nTitle: {title}\nDescription: {description}\n\nPARENT CONTEXT:\n{parentContext}\n\nEXISTING SIBLINGS (do not duplicate):\n{siblingList}\n\nHISTORICAL ACCURACY:\n{historyStats}\n\nAVERAGE HOURLY RATE: ৳{hourlyRate}/hr\n\nCODEBASE CONTEXT (if relevant):\n{codeChunks}\n\nEstimate this {entityType} using PERT. Return JSON only."
    }]
  }],
  "generationConfig": {
    "responseMimeType": "application/json",
    "responseSchema": {
      "type": "OBJECT",
      "properties": {
        "optimisticHours":    { "type": "NUMBER" },
        "mostLikelyHours":    { "type": "NUMBER" },
        "pessimisticHours":   { "type": "NUMBER" },
        "pertHours":          { "type": "NUMBER" },
        "priority":           { "type": "STRING" },
        "storyPoints":        { "type": "INTEGER" },
        "estimatedBudgetBDT": { "type": "NUMBER" },
        "confidence":         { "type": "STRING", "enum": ["High","Medium","Low"] },
        "rationale":          { "type": "STRING" },
        "risks":              { "type": "ARRAY", "items": { "type": "STRING" } }
      },
      "required": ["optimisticHours","mostLikelyHours","pessimisticHours","pertHours","priority","rationale","confidence"]
    }
  }
}
```

### Template B: Child Item Suggestions (Step-by-Step)

One level down only. Used when user wants to see suggestions before committing.

```json
{
  "contents": [{
    "parts": [{
      "text": "CHILD ITEM SUGGESTION REQUEST\n\nParent Type: {parentType} → Child Type: {childType}\nParent: {parentTitle}\nDescription: {parentDescription}\n\nEXISTING CHILDREN (avoid duplicating):\n{existingChildren}\n\nPROJECT CONTEXT:\n{projectContext}\n\nSuggest {minChildren}–{maxChildren} {childType}s. Each must be distinct and non-overlapping. Return JSON only."
    }]
  }],
  "generationConfig": {
    "responseMimeType": "application/json",
    "responseSchema": {
      "type": "OBJECT",
      "properties": {
        "children": {
          "type": "ARRAY",
          "items": {
            "type": "OBJECT",
            "properties": {
              "title":           { "type": "STRING" },
              "description":     { "type": "STRING" },
              "optimisticHours": { "type": "NUMBER" },
              "mostLikelyHours": { "type": "NUMBER" },
              "pessimisticHours":{ "type": "NUMBER" },
              "priority":        { "type": "STRING" }
            },
            "required": ["title","description","mostLikelyHours","priority"]
          }
        },
        "rationale": { "type": "STRING" }
      }
    }
  }
}
```

### Template C: Full Cascade (Overall Breakdown)

Used when user clicks "Full Breakdown" — generates Epic → Features → UserStories → Tasks in one call.  
**Warning**: This can use 3,000–5,000 output tokens. Only offer for epics with <5 existing features.

```json
{
  "contents": [{
    "parts": [{
      "text": "FULL BREAKDOWN REQUEST\n\nEpic: {epicTitle}\nDescription: {epicDescription}\n\nProject: {projectName}\nTeam velocity: {velocity} story points/sprint\n\nGenerate a complete Feature → UserStory → Task breakdown. Max 5 Features, max 4 UserStories per Feature, max 5 Tasks per UserStory. Keep scope realistic. Return JSON."
    }]
  }],
  "generationConfig": {
    "responseMimeType": "application/json",
    "responseSchema": {
      "type": "OBJECT",
      "properties": {
        "features": {
          "type": "ARRAY",
          "items": {
            "type": "OBJECT",
            "properties": {
              "title": { "type": "STRING" },
              "description": { "type": "STRING" },
              "userStories": {
                "type": "ARRAY",
                "items": {
                  "type": "OBJECT",
                  "properties": {
                    "title":              { "type": "STRING" },
                    "description":        { "type": "STRING" },
                    "acceptanceCriteria": { "type": "STRING" },
                    "mostLikelyHours":    { "type": "NUMBER" },
                    "tasks": {
                      "type": "ARRAY",
                      "items": {
                        "type": "OBJECT",
                        "properties": {
                          "title":           { "type": "STRING" },
                          "optimisticHours": { "type": "NUMBER" },
                          "mostLikelyHours": { "type": "NUMBER" },
                          "pessimisticHours":{ "type": "NUMBER" }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
      }
    }
  }
}
```

---

## Caching Strategy

To avoid redundant Gemini calls and reduce cost:

| Cache Key | TTL | What's Cached |
|-----------|-----|--------------|
| `project-stats:{projectId}` | 15 min | PM knowledge snapshot |
| `history-stats:{projectId}:{entityType}` | 30 min | Historical accuracy stats |
| `siblings:{parentId}:{parentType}` | 5 min | Sibling name list |
| `code-chunk:{queryHash}` | 60 min | Top-K code chunks for query |

Use `IMemoryCache` (already registered in `CapacityPlanningService` — same pattern).

---

## Hourly Rate Calculation (BDT)

Source: `SalaryHistory` table — already in the domain model.

```csharp
// PmKnowledgeService
public async Task<decimal> GetAverageHourlyRateBdtAsync(int projectId)
{
    // Get team members allocated to this project
    var allocatedUserIds = await _context.ProjectResourceAllocations
        .Where(a => a.ProjectId == projectId)
        .Select(a => a.UserId)
        .Distinct()
        .ToListAsync();

    // Get their latest salary and convert to hourly (BDT)
    // Assumption: monthly salary / 22 working days / 8 hours = hourly rate
    var rates = await _context.SalaryHistories
        .Where(s => allocatedUserIds.Contains(s.UserId))
        .GroupBy(s => s.UserId)
        .Select(g => g.OrderByDescending(s => s.EffectiveDate).First().Amount)
        .ToListAsync();

    if (!rates.Any()) return 800m; // fallback BDT hourly rate
    return rates.Average() / 22 / 8;
}
```
