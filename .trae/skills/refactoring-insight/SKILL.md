---
name: "refactoring-insight"
description: "Use when /refactoring-insight analyzes the codebase for refactoring opportunities including large files, code duplication, god classes, naming inconsistencies, tight coupling, and missing abstractions. Invoke when the user asks for refactoring insight, refactoring analysis, code quality analysis, architecture review, or wants to improve code architecture."
trigger:
  - "/refactoring-insight"
  - "refactoring insight"
  - "refactoring analysis"
  - "code quality analysis"
  - "architecture review"
  - "code smells"
  - "what needs refactoring"
---

# Refactoring Insight

Deep codebase analysis skill that identifies structural problems and produces prioritized refactoring recommendations.

## When to Invoke

This Skill is triggered when the user:

- Uses the slash command `/refactoring-insight`
- Asks for "refactoring insight", "refactoring analysis", "code quality analysis", or "architecture review"
- Asks "where are the code smells?" or "what needs refactoring?"
- Wants to understand what should be refactored in the codebase
- Requests a structural or architectural health check of the project

## Analysis Dimensions

Run all 6 dimensions in parallel where possible. For each dimension, use search agents to gather data, then synthesize findings.

### 1. Large Files / God Classes

- Find all .cs files over 300 lines, sorted by line count descending
- Identify partial classes and sum their total line count across files
- Flag classes with 15+ methods or constructors taking 8+ parameters
- Focus on: Views/, ViewModels/, Services/, plugins/

**Output**: Table of files with line counts and responsibility summary.

### 2. Code Duplication

Search for these specific duplication patterns:

- **Service boilerplate**: Repeated DI registration, `new` instantiation instead of DI
- **Data service pattern**: Services that fetch/parse/transform data similarly (Load → Map → Save)
- **Localization pattern**: `private readonly LocalizationService _localizationService = new();` and `L()` helper method repetitions
- **Helper method duplication**: Methods like `ResolveUnifiedMainRadiusValue`, `NormalizeConfig`, `ParticleState` classes copied across files
- **Error handling pattern**: Identical try-catch blocks repeated in multiple methods
- **Settings snapshot pattern**: `_settingsFacade.Settings.LoadSnapshot<T>(scope)` call sites

**Output**: List of duplicated patterns with file locations and line numbers.

### 3. Tight Coupling

- Services instantiated via `new` instead of DI injection
- ViewModels directly accessing infrastructure-layer APIs (e.g., `LoadSnapshot/SaveSnapshot`)
- Hard-coded dependencies (GitHub repo owner/name, default values)
- `Application.Current` upcasting to access services: `(Application.Current as App)?.SomeService`
- Platform-specific code embedded in cross-platform services without interface abstraction

**Output**: Table of coupling violations with severity (high/medium/low).

### 4. Naming Inconsistencies

- Service suffix inconsistency: `Service` vs `Store` vs `Helper` vs `Provider` vs `Manager` vs `Factory` for similar responsibilities
- Model suffix inconsistency: `Snapshot` vs `State` vs `Types` for similar concepts
- Platform prefix inconsistency: `Windows`/`Linux` full name vs `Mac` abbreviation
- Confusing names: services with similar names but different responsibilities (e.g., `NotificationService` vs `NotificationListenerService`)

**Output**: Categorized list of naming inconsistencies.

### 5. Missing Abstractions

- Services without corresponding interfaces (check for `I<ServiceName>` pattern)
- Common patterns that could be extracted into base classes:
  - `SettingsPageViewModelBase` for shared ViewModel boilerplate
  - `JsonFileSettingsService<TSnapshot>` for repeated settings persistence
  - `SettingsDomainServiceBase<TState>` for Load-Map-Save pattern
  - `DesktopComponentWidgetBase` for shared Widget code
  - `ComponentEditorViewBase` enhancements (e.g., `_suppressEvents` pattern)
- Static singleton/Factory providers repeating thread-safe lazy-load boilerplate

**Output**: List of missing abstractions with proposed base class/interface names.

### 6. Misplaced Responsibilities

- Files in wrong directories (e.g., data access in Settings/, UI services mixed with data services)
- ViewModels containing business logic or file system operations
- Widget code-behind files with excessive logic (>200 lines)
- Platform-specific services not organized into subdirectories

**Output**: List of misplaced files/classes with recommended new locations.

## Output Validation

Before delivering the report, verify ALL of the following:

1. **Completeness**: All 6 analysis dimensions have been executed and have findings or an explicit "no issues found" statement.
2. **Priority assignment**: Every finding has exactly one priority level (P0/P1/P2/P3) with justification matching the priority criteria.
3. **File references**: Every finding includes at least one affected file path that exists in the codebase (verify via file lookup).
4. **Line numbers**: Every file reference includes specific line numbers or ranges that point to the described problem.
5. **Actionable recommendation**: Every finding includes a concrete recommended action (not just "fix this" — specify what to extract, rename, merge, or move).
6. **Summary table**: The report begins with a summary table containing total metrics (file count analyzed, duplication instances, coupling violations, etc.).
7. **No false positives**: Cross-check at least P0 and P1 findings by reading the referenced code to confirm the problem actually exists.

If any validation check fails, correct the finding before including it in the final report.

## Output Format

Produce a structured report with:

1. **Summary table**: Total metrics (file count, duplication count, etc.)
2. **Priority-ranked findings**: P0 (must fix), P1 (should fix), P2 (recommended), P3 (nice to have)
3. **Each finding includes**: Problem description, affected files with links, specific line numbers, recommended action, estimated impact

### Priority Criteria

- **P0**: Files over 1000 lines with mixed responsibilities; patterns duplicated 10+ times; god classes with 20+ dependencies
- **P1**: Patterns duplicated 5-9 times; services without interfaces that are widely used; DI bypass affecting testability
- **P2**: Patterns duplicated 3-4 times; naming inconsistencies affecting readability; misplaced files
- **P3**: Minor naming variations; single-instance duplications; organizational improvements

## Verification

After completing the analysis, run ALL of the following checks before delivering the report:

1. **Dimension coverage**: Confirm all 6 analysis dimensions produced findings or an explicit "no issues found" statement. Count: exactly 6 dimension sections must appear in the output.
2. **File existence**: For every file path referenced in a finding, verify the file exists via `Glob` or `Read`. Remove or correct any finding whose primary file path does not resolve.
3. **Priority consistency**: Confirm every finding has exactly one priority (P0/P1/P2/P3) and that the priority matches the criteria in the Priority Criteria section (e.g., P0 requires 1000+ lines or 10+ duplications).
4. **Line number accuracy**: For P0 and P1 findings, re-read the referenced code at the cited line numbers to confirm the described problem actually exists at that location.
5. **Actionable check**: Every recommendation must specify a concrete action (extract to base class, rename X to Y, merge file A into file B, add interface I). Reject vague recommendations like "improve this" or "consider refactoring".
6. **Summary metrics match**: The summary table totals must equal the actual count of findings per dimension. Cross-check: sum of duplication instances in summary = count of duplication findings, etc.

If any check fails, correct the finding before including it. If correction is not possible, remove the finding and note the gap.

## Routing

### Stop Conditions

- All 6 dimensions have been analyzed and the report passes all Verification checks.
- The user explicitly stops the analysis early.
- The target workspace contains fewer than 10 source files (report "codebase too small for meaningful analysis" with a brief summary instead of the full report).

### After Completion

1. **If P0 findings exist**: Recommend the user address P0 items first. Suggest invoking `/refactoring-insight` again after P0 fixes to re-evaluate.
2. **If the user wants to act on findings**: For each accepted finding, the concrete recommendation already specifies the action (extract, rename, merge, move). Execute the action in a separate task — do not mix refactoring execution with this analysis Skill.
3. **If no significant issues found (P2/P3 only)**: Report that the codebase structure is healthy. List P2/P3 items as optional improvements. No follow-up needed.
4. **If the user requests deeper analysis on a specific dimension**: Re-invoke this Skill with a scoped target (e.g., "analyze only the Services/ directory for tight coupling"). Do not create a new Skill for scoped analysis.
5. **Handoff to other Skills**: If findings reveal needs for other workflows, route as follows:
   - Security concerns → `/security-scan`
   - Test coverage gaps → suggest adding tests (not owned by this Skill)
   - Architecture documentation gaps → update `docs/ARCHITECTURE.md` (not owned by this Skill)

### Failure Boundary

- This Skill is read-only analysis. It does not modify source code, create files, or change project configuration.
- If the codebase is not a .NET/C# project, this Skill's project-specific context does not apply. Adapt dimension targets or report "not applicable".
- If search tools return no results for a dimension, report "no data available" for that dimension rather than guessing.

## Project-Specific Context

This skill is aware of the LanMountainDesktop project structure:

- `LanMountainDesktop/Services/` — Business and infrastructure services
- `LanMountainDesktop/Services/Settings/` — Settings subsystem
- `LanMountainDesktop/ViewModels/` — View models
- `LanMountainDesktop/Views/Components/` — Desktop widget components
- `LanMountainDesktop/Views/ComponentEditors/` — Widget editor views
- `LanMountainDesktop/plugins/` — Plugin runtime
- `LanMountainDesktop.PluginSdk/` — Plugin SDK public API
- `LanMountainDesktop.Shared.Contracts/` — Host/plugin shared contracts
- `LanMountainDesktop.Appearance/` — Appearance and corner radius infrastructure

When analyzing, respect the project's architectural boundaries documented in `docs/ARCHITECTURE.md` and `docs/ECOSYSTEM_BOUNDARIES.md`.
