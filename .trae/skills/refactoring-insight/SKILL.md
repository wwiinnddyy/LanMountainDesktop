---
name: "refactoring-insight"
description: "Analyzes codebase for refactoring opportunities: large files, code duplication, god classes, naming inconsistencies, tight coupling, and missing abstractions. Invoke when user asks for refactoring insight/analysis or wants to improve code architecture."
---

# Refactoring Insight

Deep codebase analysis skill that identifies structural problems and produces prioritized refactoring recommendations.

## When to Invoke

- User asks for "refactoring insight", "refactoring analysis", "code quality analysis", "architecture review"
- User wants to understand what should be refactored in the codebase
- User asks "where are the code smells?" or "what needs refactoring?"

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
