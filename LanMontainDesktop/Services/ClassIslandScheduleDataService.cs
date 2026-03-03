using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using LanMontainDesktop.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LanMontainDesktop.Services;

public interface IClassIslandScheduleDataService
{
    ClassIslandScheduleReadResult Load(string? inputPath = null, string? profileFileName = null);

    bool TryResolveClassPlanForDate(
        ClassIslandScheduleSnapshot snapshot,
        DateOnly date,
        out ClassIslandResolvedClassPlan resolvedClassPlan);

    IReadOnlyList<int> GetCyclePositionsByDate(
        DateOnly referenceDate,
        ClassIslandScheduleCycleRule cycleRule);
}

public sealed class ClassIslandScheduleDataService : IClassIslandScheduleDataService
{
    private static readonly Guid DefaultClassPlanGroupId = new("ACAF4EF0-E261-4262-B941-34EA93CB4369");
    private static readonly Guid GlobalClassPlanGroupId = Guid.Empty;

    private const int TempClassPlanGroupTypeOverride = 0;
    private const int TempClassPlanGroupTypeInherit = 1;

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly IDeserializer CsesDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public ClassIslandScheduleReadResult Load(string? inputPath = null, string? profileFileName = null)
    {
        var warnings = new List<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                inputPath = ResolveImportedSchedulePathFromAppSettings();
            }

            var source = ResolveSource(inputPath, profileFileName, warnings);
            if (source is null)
            {
                return ClassIslandScheduleReadResult.Fail(
                    errorCode: "schedule_source_not_found",
                    errorMessage: "Cannot locate ClassIsland Settings.json or profile file.",
                    warnings: warnings);
            }

            if (!File.Exists(source.ProfilePath))
            {
                return ClassIslandScheduleReadResult.Fail(
                    errorCode: "schedule_profile_not_found",
                    errorMessage: $"ClassIsland profile file not found: {source.ProfilePath}",
                    warnings: warnings);
            }

            ClassIslandScheduleSnapshot snapshot;
            if (source.SourceKind == ScheduleSourceKind.Cses)
            {
                snapshot = ParseCsesSnapshot(source);
            }
            else
            {
                var cycleRule = ParseCycleRule(source.SettingsPath, warnings);
                var profileJson = ReadJson(source.ProfilePath);
                snapshot = ParseProfileSnapshot(profileJson.RootElement, source, cycleRule);
            }

            return ClassIslandScheduleReadResult.Ok(snapshot, warnings);
        }
        catch (Exception ex)
        {
            return ClassIslandScheduleReadResult.Fail(
                errorCode: "schedule_load_failed",
                errorMessage: ex.Message,
                warnings: warnings);
        }
    }

    public bool TryResolveClassPlanForDate(
        ClassIslandScheduleSnapshot snapshot,
        DateOnly date,
        out ClassIslandResolvedClassPlan resolvedClassPlan)
    {
        resolvedClassPlan = default!;

        if (snapshot.OrderedSchedules.TryGetValue(date, out var orderedClassPlanId) &&
            snapshot.ClassPlans.TryGetValue(orderedClassPlanId, out var orderedClassPlan) &&
            (!orderedClassPlan.IsOverlay || snapshot.IsOverlayClassPlanEnabled))
        {
            resolvedClassPlan = new ClassIslandResolvedClassPlan(orderedClassPlanId, orderedClassPlan, "ordered_schedule");
            return true;
        }

        if (snapshot.TempClassPlanId.HasValue &&
            snapshot.ClassPlans.TryGetValue(snapshot.TempClassPlanId.Value, out var tempClassPlan) &&
            snapshot.TempClassPlanSetupDate.HasValue &&
            snapshot.TempClassPlanSetupDate.Value >= date)
        {
            resolvedClassPlan = new ClassIslandResolvedClassPlan(snapshot.TempClassPlanId.Value, tempClassPlan, "temp_class_plan");
            return true;
        }

        var selectedGroupId = snapshot.SelectedClassPlanGroupId ?? DefaultClassPlanGroupId;
        var tempGroupActive = snapshot.IsTempClassPlanGroupEnabled &&
                              snapshot.TempClassPlanGroupId.HasValue &&
                              (!snapshot.TempClassPlanGroupExpireDate.HasValue ||
                               snapshot.TempClassPlanGroupExpireDate.Value >= date);

        var cyclePositions = GetCyclePositionsByDate(date, snapshot.CycleRule);

        var matched = snapshot.ClassPlans
            .Where(kvp => CheckRegularClassPlan(
                snapshot,
                kvp.Value,
                date,
                selectedGroupId,
                tempGroupActive,
                cyclePositions))
            .OrderByDescending(kvp => GetGroupPriority(kvp.Value.AssociatedGroupId, snapshot, selectedGroupId))
            .ThenBy(kvp => kvp.Value.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (matched.Value is null)
        {
            return false;
        }

        resolvedClassPlan = new ClassIslandResolvedClassPlan(matched.Key, matched.Value, "regular");
        return true;
    }

    public IReadOnlyList<int> GetCyclePositionsByDate(
        DateOnly referenceDate,
        ClassIslandScheduleCycleRule cycleRule)
    {
        var maxCycle = Math.Clamp(cycleRule.MultiWeekRotationMaxCycle, 2, 32);
        var result = Enumerable.Repeat(-1, maxCycle + 1).ToArray();
        result[0] = -1;
        result[1] = -1;

        if (!cycleRule.SingleWeekStartDate.HasValue)
        {
            return result;
        }

        var totalElapsedWeeks = (int)Math.Floor(
            (referenceDate.ToDateTime(TimeOnly.MinValue) - cycleRule.SingleWeekStartDate.Value.ToDateTime(TimeOnly.MinValue)).TotalDays / 7d);

        for (var cycleLength = 2; cycleLength <= maxCycle; cycleLength++)
        {
            var cycleOffset = cycleLength < cycleRule.MultiWeekRotationOffset.Count
                ? cycleRule.MultiWeekRotationOffset[cycleLength]
                : 0;
            var positionInCycle = (totalElapsedWeeks + cycleOffset) % cycleLength;
            if (positionInCycle < 0)
            {
                positionInCycle += cycleLength;
            }

            result[cycleLength] = positionInCycle + 1;
        }

        return result;
    }

    private static string? ResolveImportedSchedulePathFromAppSettings()
    {
        try
        {
            var snapshot = new AppSettingsService().Load();
            if (snapshot.ImportedClassSchedules.Count == 0)
            {
                return null;
            }

            var activeId = snapshot.ActiveImportedClassScheduleId?.Trim() ?? string.Empty;
            ImportedClassScheduleSnapshot? active = null;
            if (!string.IsNullOrWhiteSpace(activeId))
            {
                active = snapshot.ImportedClassSchedules
                    .FirstOrDefault(item => string.Equals(item.Id, activeId, StringComparison.OrdinalIgnoreCase));
            }

            active ??= snapshot.ImportedClassSchedules[0];
            return active.FilePath;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ResolvedSource(
        ScheduleSourceKind SourceKind,
        string SourceRootPath,
        string? SettingsPath,
        string ProfilePath,
        string ProfileFileName);

    private enum ScheduleSourceKind
    {
        ClassIslandProfile = 0,
        Cses = 1
    }

    private static ResolvedSource? ResolveSource(string? inputPath, string? profileFileName, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(inputPath))
        {
            var explicitSource = ResolveSourceFromInput(inputPath, profileFileName, warnings);
            if (explicitSource is not null)
            {
                return explicitSource;
            }
        }

        foreach (var root in GetDefaultRootCandidates())
        {
            var source = ResolveSourceFromRoot(root, profileFileName, warnings);
            if (source is not null)
            {
                return source;
            }
        }

        return null;
    }

    private static ResolvedSource? ResolveSourceFromInput(string inputPath, string? profileFileName, List<string> warnings)
    {
        var fullPath = Path.GetFullPath(inputPath.Trim());
        if (File.Exists(fullPath))
        {
            if (string.Equals(Path.GetFileName(fullPath), "Settings.json", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveSourceFromRoot(Path.GetDirectoryName(fullPath), profileFileName, warnings);
            }

            var extension = Path.GetExtension(fullPath);
            if (IsCsesExtension(extension))
            {
                var fileName = Path.GetFileName(fullPath);
                return new ResolvedSource(
                    SourceKind: ScheduleSourceKind.Cses,
                    SourceRootPath: Path.GetDirectoryName(fullPath) ?? Path.GetPathRoot(fullPath) ?? string.Empty,
                    SettingsPath: null,
                    ProfilePath: fullPath,
                    ProfileFileName: fileName);
            }

            if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(fullPath);
                var parent = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(parent) &&
                    string.Equals(Path.GetFileName(parent), "Profiles", StringComparison.OrdinalIgnoreCase))
                {
                    var root = Path.GetDirectoryName(parent) ?? parent;
                    return new ResolvedSource(
                        SourceKind: ScheduleSourceKind.ClassIslandProfile,
                        SourceRootPath: root,
                        SettingsPath: GetSettingsPath(root),
                        ProfilePath: fullPath,
                        ProfileFileName: fileName);
                }

                return new ResolvedSource(
                    SourceKind: ScheduleSourceKind.ClassIslandProfile,
                    SourceRootPath: parent ?? Path.GetPathRoot(fullPath) ?? string.Empty,
                    SettingsPath: null,
                    ProfilePath: fullPath,
                    ProfileFileName: fileName);
            }
        }

        if (Directory.Exists(fullPath))
        {
            var candidates = new[]
            {
                fullPath,
                Path.Combine(fullPath, "Data"),
                Path.Combine(fullPath, "ClassIsland", "Data")
            };

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var source = ResolveSourceFromRoot(candidate, profileFileName, warnings);
                if (source is not null)
                {
                    return source;
                }
            }
        }

        return null;
    }

    private static bool IsCsesExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return string.Equals(extension, ".cses", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static ResolvedSource? ResolveSourceFromRoot(string? root, string? profileFileName, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return null;
        }

        var settingsPath = GetSettingsPath(root);
        var profilesPath = Path.Combine(root, "Profiles");
        if (!Directory.Exists(profilesPath))
        {
            warnings.Add($"ClassIsland profiles folder missing: {profilesPath}");
            return null;
        }

        var profileName = profileFileName?.Trim();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            profileName = ResolveSelectedProfileName(settingsPath);
        }

        if (string.IsNullOrWhiteSpace(profileName))
        {
            profileName = "Default.json";
        }

        var profilePath = Path.Combine(profilesPath, profileName);
        if (!File.Exists(profilePath))
        {
            var fallback = Directory.GetFiles(profilesPath, "*.json")
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fallback))
            {
                warnings.Add($"No profile json found under {profilesPath}");
                return null;
            }

            profilePath = fallback;
            profileName = Path.GetFileName(fallback);
            warnings.Add($"Selected profile not found, fallback to {profileName}");
        }

        return new ResolvedSource(
            SourceKind: ScheduleSourceKind.ClassIslandProfile,
            SourceRootPath: root,
            SettingsPath: settingsPath,
            ProfilePath: profilePath,
            ProfileFileName: profileName);
    }

    private static IEnumerable<string> GetDefaultRootCandidates()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClassIsland", "Data"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClassIsland"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassIsland", "Data"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassIsland")
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetSettingsPath(string rootPath)
    {
        return Path.Combine(rootPath, "Settings.json");
    }

    private static string? ResolveSelectedProfileName(string? settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
        {
            return null;
        }

        using var json = ReadJson(settingsPath);
        if (TryGetProperty(json.RootElement, "SelectedProfile", out var selectedProfile) &&
            selectedProfile.ValueKind == JsonValueKind.String)
        {
            return selectedProfile.GetString();
        }

        return null;
    }

    private static ClassIslandScheduleCycleRule ParseCycleRule(string? settingsPath, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
        {
            warnings.Add("ClassIsland Settings.json not found, using default cycle rule.");
            return new ClassIslandScheduleCycleRule(null, 4, new List<int> { -1, -1, 0, 0, 0 });
        }

        using var json = ReadJson(settingsPath);
        var root = json.RootElement;
        var singleWeekStartDate = TryReadDateOnly(root, "SingleWeekStartTime");
        var maxCycle = TryReadInt(root, "MultiWeekRotationMaxCycle", 4);
        var offsetList = ReadIntList(root, "MultiWeekRotationOffset");
        if (offsetList.Count < 2)
        {
            offsetList = new List<int> { -1, -1, 0, 0, 0 };
        }

        return new ClassIslandScheduleCycleRule(
            singleWeekStartDate,
            Math.Clamp(maxCycle, 2, 32),
            offsetList);
    }

    private static ClassIslandScheduleSnapshot ParseProfileSnapshot(
        JsonElement root,
        ResolvedSource source,
        ClassIslandScheduleCycleRule cycleRule)
    {
        var subjects = ReadSubjects(root);
        var timeLayouts = ReadTimeLayouts(root);
        var classPlans = ReadClassPlans(root);
        var orderedSchedules = ReadOrderedSchedules(root);
        var groups = ReadClassPlanGroups(root);

        return new ClassIslandScheduleSnapshot(
            SourceRootPath: source.SourceRootPath,
            ProfilePath: source.ProfilePath,
            ProfileFileName: source.ProfileFileName,
            LoadedAt: DateTimeOffset.Now,
            CycleRule: cycleRule,
            SelectedClassPlanGroupId: TryReadGuid(root, "SelectedClassPlanGroupId"),
            TempClassPlanGroupId: TryReadGuid(root, "TempClassPlanGroupId"),
            IsTempClassPlanGroupEnabled: TryReadBool(root, "IsTempClassPlanGroupEnabled", false),
            TempClassPlanGroupExpireDate: TryReadDateOnly(root, "TempClassPlanGroupExpireTime"),
            TempClassPlanGroupType: TryReadInt(root, "TempClassPlanGroupType", TempClassPlanGroupTypeInherit),
            TempClassPlanId: TryReadGuid(root, "TempClassPlanId"),
            TempClassPlanSetupDate: TryReadDateOnly(root, "TempClassPlanSetupTime"),
            IsOverlayClassPlanEnabled: TryReadBool(root, "IsOverlayClassPlanEnabled", false),
            OverlayClassPlanId: TryReadGuid(root, "OverlayClassPlanId"),
            Subjects: subjects,
            TimeLayouts: timeLayouts,
            ClassPlans: classPlans,
            OrderedSchedules: orderedSchedules,
            ClassPlanGroups: groups);
    }

    private static ClassIslandScheduleSnapshot ParseCsesSnapshot(ResolvedSource source)
    {
        var yaml = File.ReadAllText(source.ProfilePath);
        var csesProfile = CsesDeserializer.Deserialize<CsesProfileDto>(yaml) ?? new CsesProfileDto();

        var subjects = new Dictionary<Guid, ClassIslandSubject>();
        var subjectIdByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawSubject in csesProfile.Subjects ?? [])
        {
            var subjectName = (rawSubject.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(subjectName))
            {
                continue;
            }

            if (!subjectIdByName.TryGetValue(subjectName, out var subjectId))
            {
                subjectId = Guid.NewGuid();
                subjectIdByName[subjectName] = subjectId;
            }

            subjects[subjectId] = new ClassIslandSubject(
                Id: subjectId,
                Name: subjectName,
                Initial: rawSubject.SimplifiedName?.Trim(),
                TeacherName: rawSubject.Teacher?.Trim(),
                IsOutDoor: null);
        }

        var timeLayouts = new Dictionary<Guid, ClassIslandTimeLayout>();
        var classPlans = new Dictionary<Guid, ClassIslandClassPlan>();

        foreach (var schedule in csesProfile.Schedules ?? [])
        {
            var rawClasses = schedule.Classes ?? [];
            if (rawClasses.Count == 0)
            {
                continue;
            }

            var layoutId = Guid.NewGuid();
            var layoutItems = new List<ClassIslandTimeLayoutItem>();
            var classInfos = new List<ClassIslandClassInfo>();

            for (var i = 0; i < rawClasses.Count; i++)
            {
                var rawClass = rawClasses[i];
                var start = ParseCsesTime(rawClass.StartTime);
                var end = ParseCsesTime(rawClass.EndTime);
                if (end < start)
                {
                    (start, end) = (end, start);
                }

                layoutItems.Add(new ClassIslandTimeLayoutItem(
                    StartTime: start,
                    EndTime: end,
                    TimeType: 0,
                    IsHiddenByDefault: false,
                    DefaultSubjectId: null,
                    BreakName: null));

                var subjectName = (rawClass.Subject ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(subjectName))
                {
                    classInfos.Add(new ClassIslandClassInfo(null, IsEnabled: true));
                }
                else
                {
                    if (!subjectIdByName.TryGetValue(subjectName, out var subjectId))
                    {
                        subjectId = Guid.NewGuid();
                        subjectIdByName[subjectName] = subjectId;
                        subjects[subjectId] = new ClassIslandSubject(
                            Id: subjectId,
                            Name: subjectName,
                            Initial: null,
                            TeacherName: null,
                            IsOutDoor: null);
                    }

                    classInfos.Add(new ClassIslandClassInfo(subjectId, IsEnabled: true));
                }

                if (i >= rawClasses.Count - 1)
                {
                    continue;
                }

                var nextStart = ParseCsesTime(rawClasses[i + 1].StartTime);
                if (nextStart > end)
                {
                    layoutItems.Add(new ClassIslandTimeLayoutItem(
                        StartTime: end,
                        EndTime: nextStart,
                        TimeType: 1,
                        IsHiddenByDefault: false,
                        DefaultSubjectId: null,
                        BreakName: null));
                }
            }

            timeLayouts[layoutId] = new ClassIslandTimeLayout(
                Id: layoutId,
                Name: string.IsNullOrWhiteSpace(schedule.Name) ? "CSES" : schedule.Name.Trim(),
                Items: layoutItems);

            var weekCountDiv = ParseCsesWeekValue(schedule.Weeks);
            var classPlanId = Guid.NewGuid();
            classPlans[classPlanId] = new ClassIslandClassPlan(
                Id: classPlanId,
                Name: string.IsNullOrWhiteSpace(schedule.Name) ? "CSES" : schedule.Name.Trim(),
                TimeLayoutId: layoutId,
                Rule: new ClassIslandTimeRule(
                    WeekDay: ParseCsesWeekDay(schedule.EnableDay),
                    WeekCountDiv: weekCountDiv,
                    WeekCountDivTotal: weekCountDiv == 0 ? 0 : 2),
                Classes: classInfos,
                IsEnabled: true,
                IsOverlay: false,
                OverlaySourceId: null,
                OverlaySetupDate: null,
                AssociatedGroupId: DefaultClassPlanGroupId);
        }

        var classPlanGroups = new Dictionary<Guid, ClassIslandClassPlanGroup>
        {
            [DefaultClassPlanGroupId] = new ClassIslandClassPlanGroup(DefaultClassPlanGroupId, "Default", IsGlobal: false),
            [GlobalClassPlanGroupId] = new ClassIslandClassPlanGroup(GlobalClassPlanGroupId, "Global", IsGlobal: true)
        };

        return new ClassIslandScheduleSnapshot(
            SourceRootPath: source.SourceRootPath,
            ProfilePath: source.ProfilePath,
            ProfileFileName: source.ProfileFileName,
            LoadedAt: DateTimeOffset.Now,
            CycleRule: new ClassIslandScheduleCycleRule(null, 4, new List<int> { -1, -1, 0, 0, 0 }),
            SelectedClassPlanGroupId: DefaultClassPlanGroupId,
            TempClassPlanGroupId: null,
            IsTempClassPlanGroupEnabled: false,
            TempClassPlanGroupExpireDate: null,
            TempClassPlanGroupType: TempClassPlanGroupTypeInherit,
            TempClassPlanId: null,
            TempClassPlanSetupDate: null,
            IsOverlayClassPlanEnabled: false,
            OverlayClassPlanId: null,
            Subjects: subjects,
            TimeLayouts: timeLayouts,
            ClassPlans: classPlans,
            OrderedSchedules: new Dictionary<DateOnly, Guid>(),
            ClassPlanGroups: classPlanGroups);
    }

    private static bool CheckRegularClassPlan(
        ClassIslandScheduleSnapshot snapshot,
        ClassIslandClassPlan classPlan,
        DateOnly date,
        Guid selectedGroupId,
        bool tempGroupActive,
        IReadOnlyList<int> cyclePositions)
    {
        if (classPlan.IsOverlay || !classPlan.IsEnabled)
        {
            return false;
        }

        if (classPlan.Rule.WeekDay != (int)date.DayOfWeek)
        {
            return false;
        }

        var associatedGroup = classPlan.AssociatedGroupId ?? selectedGroupId;
        var matchGlobal = associatedGroup == GlobalClassPlanGroupId;
        var matchSelected = associatedGroup == selectedGroupId;
        var matchTemp = tempGroupActive &&
                        snapshot.TempClassPlanGroupId.HasValue &&
                        associatedGroup == snapshot.TempClassPlanGroupId.Value;

        var matchesGroup = tempGroupActive
            ? snapshot.TempClassPlanGroupType switch
            {
                TempClassPlanGroupTypeOverride => matchTemp || matchGlobal,
                TempClassPlanGroupTypeInherit => matchSelected || matchTemp || matchGlobal,
                _ => matchSelected || matchGlobal
            }
            : matchSelected || matchGlobal;

        if (!matchesGroup)
        {
            return false;
        }

        var weekCountDivTotal = classPlan.Rule.WeekCountDivTotal;
        var weekCountDiv = classPlan.Rule.WeekCountDiv;
        if (weekCountDiv == 0)
        {
            return true;
        }

        if (weekCountDivTotal <= 1 || weekCountDivTotal >= cyclePositions.Count)
        {
            return false;
        }

        return cyclePositions[weekCountDivTotal] == weekCountDiv;
    }

    private static int GetGroupPriority(Guid? associatedGroup, ClassIslandScheduleSnapshot snapshot, Guid selectedGroupId)
    {
        var group = associatedGroup ?? selectedGroupId;
        if (snapshot.TempClassPlanGroupId.HasValue && group == snapshot.TempClassPlanGroupId.Value)
        {
            return 3;
        }

        if (group == selectedGroupId)
        {
            return 2;
        }

        if (group == GlobalClassPlanGroupId)
        {
            return 1;
        }

        return 0;
    }

    private static JsonDocument ReadJson(string path)
    {
        var content = File.ReadAllText(path);
        return JsonDocument.Parse(content, JsonOptions);
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static int TryReadInt(JsonElement element, string propertyName, int fallback)
    {
        if (TryGetProperty(element, propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String &&
                int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return fallback;
    }

    private static bool TryReadBool(JsonElement element, string propertyName, bool fallback)
    {
        if (TryGetProperty(element, propertyName, out var value))
        {
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static Guid? TryReadGuid(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return ReadGuidValue(value);
    }

    private static Guid? ReadGuidValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String &&
            Guid.TryParse(element.GetString(), out var guid) &&
            guid != Guid.Empty)
        {
            return guid;
        }

        return null;
    }

    private static DateOnly? TryReadDateOnly(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            {
                return DateOnly.FromDateTime(dto.DateTime);
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            {
                return DateOnly.FromDateTime(dt);
            }

            if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            {
                return dateOnly;
            }
        }

        return null;
    }

    private static List<int> ReadIntList(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var element))
        {
            return [];
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number) ? number : 0)
                .ToList();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var list = new List<int>();
            foreach (var property in element.EnumerateObject())
            {
                if (!int.TryParse(property.Name, out var index))
                {
                    continue;
                }

                while (list.Count <= index)
                {
                    list.Add(0);
                }

                var value = property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number)
                    ? number
                    : 0;
                list[index] = value;
            }

            return list;
        }

        return [];
    }

    private static IReadOnlyDictionary<Guid, ClassIslandSubject> ReadSubjects(JsonElement root)
    {
        var result = new Dictionary<Guid, ClassIslandSubject>();
        if (!TryGetProperty(root, "Subjects", out var subjectsElement) || subjectsElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in subjectsElement.EnumerateObject())
        {
            if (!Guid.TryParse(property.Name, out var id))
            {
                continue;
            }

            var node = property.Value;
            result[id] = new ClassIslandSubject(
                Id: id,
                Name: TryGetProperty(node, "Name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                Initial: TryGetProperty(node, "Initial", out var initial) ? initial.GetString() : null,
                TeacherName: TryGetProperty(node, "TeacherName", out var teacher) ? teacher.GetString() : null,
                IsOutDoor: TryGetProperty(node, "IsOutDoor", out var outdoor) && outdoor.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? outdoor.GetBoolean()
                    : null);
        }

        return result;
    }

    private static IReadOnlyDictionary<Guid, ClassIslandTimeLayout> ReadTimeLayouts(JsonElement root)
    {
        var result = new Dictionary<Guid, ClassIslandTimeLayout>();
        if (!TryGetProperty(root, "TimeLayouts", out var layoutsElement) || layoutsElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in layoutsElement.EnumerateObject())
        {
            if (!Guid.TryParse(property.Name, out var id))
            {
                continue;
            }

            var node = property.Value;
            var items = new List<ClassIslandTimeLayoutItem>();
            if (TryGetProperty(node, "Layouts", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsElement.EnumerateArray())
                {
                    var start = ReadTimeValue(item, "StartTime", "StartSecond");
                    var end = ReadTimeValue(item, "EndTime", "EndSecond");
                    items.Add(new ClassIslandTimeLayoutItem(
                        StartTime: start,
                        EndTime: end,
                        TimeType: TryReadInt(item, "TimeType", 0),
                        IsHiddenByDefault: TryReadBool(item, "IsHideDefault", false),
                        DefaultSubjectId: TryReadGuid(item, "DefaultClassId"),
                        BreakName: TryGetProperty(item, "BreakName", out var breakName) ? breakName.GetString() : null));
                }
            }

            result[id] = new ClassIslandTimeLayout(
                Id: id,
                Name: TryGetProperty(node, "Name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                Items: items);
        }

        return result;
    }

    private static IReadOnlyDictionary<Guid, ClassIslandClassPlan> ReadClassPlans(JsonElement root)
    {
        var result = new Dictionary<Guid, ClassIslandClassPlan>();
        if (!TryGetProperty(root, "ClassPlans", out var plansElement) || plansElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in plansElement.EnumerateObject())
        {
            if (!Guid.TryParse(property.Name, out var id))
            {
                continue;
            }

            var node = property.Value;
            var classes = new List<ClassIslandClassInfo>();
            if (TryGetProperty(node, "Classes", out var classesElement) && classesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in classesElement.EnumerateArray())
                {
                    classes.Add(new ClassIslandClassInfo(
                        SubjectId: TryReadGuid(item, "SubjectId"),
                        IsEnabled: TryReadBool(item, "IsEnabled", true)));
                }
            }

            var ruleNode = TryGetProperty(node, "TimeRule", out var tr) ? tr : default;
            var rule = new ClassIslandTimeRule(
                WeekDay: TryReadInt(ruleNode, "WeekDay", 0),
                WeekCountDiv: TryReadInt(ruleNode, "WeekCountDiv", 0),
                WeekCountDivTotal: TryReadInt(ruleNode, "WeekCountDivTotal", 0));

            result[id] = new ClassIslandClassPlan(
                Id: id,
                Name: TryGetProperty(node, "Name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                TimeLayoutId: TryReadGuid(node, "TimeLayoutId") ?? Guid.Empty,
                Rule: rule,
                Classes: classes,
                IsEnabled: TryReadBool(node, "IsEnabled", true),
                IsOverlay: TryReadBool(node, "IsOverlay", false),
                OverlaySourceId: TryReadGuid(node, "OverlaySourceId"),
                OverlaySetupDate: TryReadDateOnly(node, "OverlaySetupTime"),
                AssociatedGroupId: TryReadGuid(node, "AssociatedGroup"));
        }

        return result;
    }

    private static IReadOnlyDictionary<Guid, ClassIslandClassPlanGroup> ReadClassPlanGroups(JsonElement root)
    {
        var result = new Dictionary<Guid, ClassIslandClassPlanGroup>();
        if (!TryGetProperty(root, "ClassPlanGroups", out var groupsElement) || groupsElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in groupsElement.EnumerateObject())
        {
            if (!Guid.TryParse(property.Name, out var id))
            {
                continue;
            }

            var node = property.Value;
            result[id] = new ClassIslandClassPlanGroup(
                Id: id,
                Name: TryGetProperty(node, "Name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                IsGlobal: TryReadBool(node, "IsGlobal", false));
        }

        return result;
    }

    private static IReadOnlyDictionary<DateOnly, Guid> ReadOrderedSchedules(JsonElement root)
    {
        var result = new Dictionary<DateOnly, Guid>();
        if (!TryGetProperty(root, "OrderedSchedules", out var orderedElement) || orderedElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in orderedElement.EnumerateObject())
        {
            if (!TryParseDateFromKey(property.Name, out var date))
            {
                continue;
            }

            Guid? classPlanId = null;
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                classPlanId = TryReadGuid(property.Value, "ClassPlanId");
            }
            else if (property.Value.ValueKind == JsonValueKind.String &&
                     Guid.TryParse(property.Value.GetString(), out var directId))
            {
                classPlanId = directId;
            }

            if (classPlanId.HasValue)
            {
                result[date] = classPlanId.Value;
            }
        }

        return result;
    }

    private static bool TryParseDateFromKey(string key, out DateOnly date)
    {
        if (DateOnly.TryParse(key, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(key, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            date = DateOnly.FromDateTime(dto.DateTime);
            return true;
        }

        if (DateTime.TryParse(key, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
            return true;
        }

        date = default;
        return false;
    }

    private static TimeSpan ReadTimeValue(JsonElement node, string primaryProperty, string legacyProperty)
    {
        if (TryGetProperty(node, primaryProperty, out var primary))
        {
            var parsed = ParseTimeFromElement(primary);
            if (parsed.HasValue)
            {
                return parsed.Value;
            }
        }

        if (TryGetProperty(node, legacyProperty, out var legacy))
        {
            var parsed = ParseTimeFromElement(legacy);
            if (parsed.HasValue)
            {
                return parsed.Value;
            }
        }

        return TimeSpan.Zero;
    }

    private static TimeSpan? ParseTimeFromElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var ts))
            {
                return ts;
            }

            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            {
                return dto.TimeOfDay;
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            {
                return dt.TimeOfDay;
            }
        }

        return null;
    }

    private static TimeSpan ParseCsesTime(object? value)
    {
        if (value is null)
        {
            return TimeSpan.Zero;
        }

        if (value is TimeSpan ts)
        {
            return ts;
        }

        if (value is DateTime dt)
        {
            return dt.TimeOfDay;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return TimeSpan.Zero;
        }

        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out ts))
        {
            return ts;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return dto.TimeOfDay;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
        {
            return dt.TimeOfDay;
        }

        return TimeSpan.Zero;
    }

    private static int ParseCsesWeekDay(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            if (day is >= 1 and <= 6)
            {
                return day;
            }

            if (day == 7)
            {
                return 0;
            }

            return Math.Clamp(day, 0, 6);
        }

        return text.ToLowerInvariant() switch
        {
            "monday" or "mon" or "zhouyi" or "周一" => 1,
            "tuesday" or "tue" or "zhouer" or "周二" => 2,
            "wednesday" or "wed" or "zhousan" or "周三" => 3,
            "thursday" or "thu" or "zhousi" or "周四" => 4,
            "friday" or "fri" or "zhouwu" or "周五" => 5,
            "saturday" or "sat" or "zhouliu" or "周六" => 6,
            "sunday" or "sun" or "zhouri" or "周日" or "周天" => 0,
            _ => 0
        };
    }

    private static int ParseCsesWeekValue(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var week))
        {
            return Math.Clamp(week, 0, 2);
        }

        return text.ToLowerInvariant() switch
        {
            "odd" or "single" or "singleweek" or "鍗曞懆" => 1,
            "even" or "double" or "doubleweek" or "鍙屽懆" => 2,
            _ => 0
        };
    }

    private sealed class CsesProfileDto
    {
        public List<CsesSubjectDto>? Subjects { get; set; }

        public List<CsesScheduleDto>? Schedules { get; set; }
    }

    private sealed class CsesSubjectDto
    {
        public string? Name { get; set; }

        public string? SimplifiedName { get; set; }

        public string? Teacher { get; set; }
    }

    private sealed class CsesScheduleDto
    {
        public string? Name { get; set; }

        public object? Weeks { get; set; }

        public object? EnableDay { get; set; }

        public List<CsesClassDto>? Classes { get; set; }
    }

    private sealed class CsesClassDto
    {
        public object? StartTime { get; set; }

        public object? EndTime { get; set; }

        public string? Subject { get; set; }
    }
}

