using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GitCommitAnalyzer
{
    public class GitObject
    {
        public string Type { get; set; }
        public byte[] Content { get; set; }
    }

    public class CommitInfo
    {
        public string Hash { get; set; }
        public string Parent { get; set; }
        public string Tree { get; set; }
        public string Author { get; set; }
        public string Email { get; set; }
        public long Timestamp { get; set; }
        public string Timezone { get; set; }
        public string Message { get; set; }
    }

    public class FileChange
    {
        public string Path { get; set; }
        public string ChangeType { get; set; }
    }

    public class CommitAnalysis
    {
        public string CommitHash { get; set; }
        public string Message { get; set; }
        public string Author { get; set; }
        public string Email { get; set; }
        public long Timestamp { get; set; }
        public string Date { get; set; }
        public Dictionary<string, int> Stats { get; set; }
        public List<FileChange> Changes { get; set; }
        public Dictionary<string, int> FileTypes { get; set; }
        public string Importance { get; set; }
        public List<string> ImpactAnalysis { get; set; }
        public List<string> ReviewPoints { get; set; }
        public List<KeySnippet> KeySnippets { get; set; }
    }

    public class KeySnippet
    {
        public string File { get; set; }
        public string Type { get; set; }
        public int LinesCount { get; set; }
        public string Preview { get; set; }
    }

    public class GitObjectParser
    {
        private readonly string _repoPath;
        private readonly string _objectsPath;
        private readonly Dictionary<string, CommitInfo> _commitCache = new();
        private readonly Dictionary<string, Dictionary<string, string>> _treeCache = new();

        public GitObjectParser(string repoPath)
        {
            _repoPath = repoPath;
            _objectsPath = Path.Combine(repoPath, ".git", "objects");
        }

        public GitObject ReadObject(string objHash)
        {
            if (objHash.Length < 4) return null;

            var objDir = objHash.Substring(0, 2);
            var objFile = objHash.Substring(2);
            var objPath = Path.Combine(_objectsPath, objDir, objFile);

            if (!File.Exists(objPath)) return null;

            try
            {
                var compressedData = File.ReadAllBytes(objPath);

                // 使用 zlib 解压缩
                using var ms = new MemoryStream(compressedData);
                // 跳过 zlib 头 (2 字节)
                ms.ReadByte();
                ms.ReadByte();

                using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
                using var result = new MemoryStream();
                deflate.CopyTo(result);
                var decompressed = result.ToArray();

                // 解析对象头
                var nullIdx = Array.IndexOf(decompressed, (byte)0);
                if (nullIdx == -1) return null;

                var header = Encoding.UTF8.GetString(decompressed, 0, nullIdx);
                var objType = header.Split(' ')[0];

                var content = new byte[decompressed.Length - nullIdx - 1];
                Array.Copy(decompressed, nullIdx + 1, content, 0, content.Length);

                return new GitObject { Type = objType, Content = content };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading object {objHash}: {ex.Message}");
                return null;
            }
        }

        public CommitInfo ParseCommit(string commitHash)
        {
            if (_commitCache.ContainsKey(commitHash))
                return _commitCache[commitHash];

            var obj = ReadObject(commitHash);
            if (obj == null || obj.Type != "commit")
                return null;

            var content = Encoding.UTF8.GetString(obj.Content);
            var lines = content.Split('\n');

            string parent = null, tree = null, author = null, email = null, timezone = null;
            long timestamp = 0;
            var messageLines = new List<string>();
            var inMessage = false;

            foreach (var line in lines)
            {
                if (inMessage)
                {
                    messageLines.Add(line);
                }
                else if (line.StartsWith("tree "))
                {
                    tree = line.Substring(5).Trim();
                }
                else if (line.StartsWith("parent "))
                {
                    parent = line.Substring(7).Trim();
                }
                else if (line.StartsWith("author "))
                {
                    var match = Regex.Match(line, @"^author (.+) <(.+)> (\d+) ([+-]\d+)$");
                    if (match.Success)
                    {
                        author = match.Groups[1].Value;
                        email = match.Groups[2].Value;
                        timestamp = long.Parse(match.Groups[3].Value);
                        timezone = match.Groups[4].Value;
                    }
                }
                else if (line == "")
                {
                    inMessage = true;
                }
            }

            var message = string.Join("\n", messageLines).Trim();

            var commitInfo = new CommitInfo
            {
                Hash = commitHash,
                Parent = parent,
                Tree = tree,
                Author = author ?? "Unknown",
                Email = email ?? "",
                Timestamp = timestamp,
                Timezone = timezone ?? "",
                Message = message
            };

            _commitCache[commitHash] = commitInfo;
            return commitInfo;
        }

        public Dictionary<string, string> ParseTree(string treeHash)
        {
            if (_treeCache.ContainsKey(treeHash))
                return _treeCache[treeHash];

            var obj = ReadObject(treeHash);
            if (obj == null || obj.Type != "tree")
                return new Dictionary<string, string>();

            var entries = new Dictionary<string, string>();
            var content = obj.Content;
            var idx = 0;

            while (idx < content.Length)
            {
                // 查找空格
                var spaceIdx = Array.IndexOf(content, (byte)' ', idx);
                if (spaceIdx == -1) break;

                var mode = Encoding.UTF8.GetString(content, idx, spaceIdx - idx);

                // 查找 null
                var nullIdx = Array.IndexOf(content, (byte)0, spaceIdx);
                if (nullIdx == -1) break;

                var name = Encoding.UTF8.GetString(content, spaceIdx + 1, nullIdx - spaceIdx - 1);

                // 读取 20 字节 SHA
                var shaStart = nullIdx + 1;
                var shaEnd = shaStart + 20;
                if (shaEnd > content.Length) break;

                var shaBytes = new byte[20];
                Array.Copy(content, shaStart, shaBytes, 0, 20);
                var sha = BitConverter.ToString(shaBytes).Replace("-", "").ToLower();

                entries[name] = sha;
                idx = shaEnd;
            }

            _treeCache[treeHash] = entries;
            return entries;
        }

        public (List<FileChange> Changes, Dictionary<string, int> Stats, CommitInfo Commit) GetCommitChanges(string commitHash)
        {
            var commit = ParseCommit(commitHash);
            if (commit == null)
                return (new List<FileChange>(), new Dictionary<string, int>(), null);

            var currentTree = ParseTree(commit.Tree);
            var parentTree = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(commit.Parent))
            {
                var parentCommit = ParseCommit(commit.Parent);
                if (parentCommit != null)
                {
                    parentTree = ParseTree(parentCommit.Tree);
                }
            }

            var changes = new List<FileChange>();
            var stats = new Dictionary<string, int> { ["Added"] = 0, ["Modified"] = 0, ["Deleted"] = 0 };

            var allPaths = currentTree.Keys.Union(parentTree.Keys).Distinct();

            foreach (var path in allPaths)
            {
                if (currentTree.ContainsKey(path) && !parentTree.ContainsKey(path))
                {
                    changes.Add(new FileChange { Path = path, ChangeType = "added" });
                    stats["Added"]++;
                }
                else if (!currentTree.ContainsKey(path) && parentTree.ContainsKey(path))
                {
                    changes.Add(new FileChange { Path = path, ChangeType = "deleted" });
                    stats["Deleted"]++;
                }
                else if (currentTree.GetValueOrDefault(path) != parentTree.GetValueOrDefault(path))
                {
                    changes.Add(new FileChange { Path = path, ChangeType = "modified" });
                    stats["Modified"]++;
                }
            }

            return (changes, stats, commit);
        }
    }

    public class CommitAnalyzer
    {
        private readonly GitObjectParser _parser;
        private readonly string _repoPath;

        public CommitAnalyzer(string repoPath)
        {
            _parser = new GitObjectParser(repoPath);
            _repoPath = repoPath;
        }

        public CommitAnalysis AnalyzeCommit(string commitHash)
        {
            var (changes, stats, commit) = _parser.GetCommitChanges(commitHash);
            if (commit == null)
                return null;

            var fileTypes = GetFileTypeDistribution(changes);
            var importance = AssessImportance(commit.Message, changes, stats);
            var impactAnalysis = AnalyzeImpact(changes, commit.Message);
            var reviewPoints = GenerateReviewPoints(changes, commit.Message);
            var keySnippets = GetKeySnippets(changes);

            return new CommitAnalysis
            {
                CommitHash = commitHash,
                Message = commit.Message,
                Author = commit.Author,
                Email = commit.Email,
                Timestamp = commit.Timestamp,
                Date = DateTimeOffset.FromUnixTimeSeconds(commit.Timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                Stats = stats,
                Changes = changes,
                FileTypes = fileTypes,
                Importance = importance,
                ImpactAnalysis = impactAnalysis,
                ReviewPoints = reviewPoints,
                KeySnippets = keySnippets
            };
        }

        private Dictionary<string, int> GetFileTypeDistribution(List<FileChange> changes)
        {
            var fileTypes = new Dictionary<string, int>();
            foreach (var change in changes)
            {
                var ext = Path.GetExtension(change.Path);
                if (string.IsNullOrEmpty(ext)) ext = "no_extension";
                if (!fileTypes.ContainsKey(ext)) fileTypes[ext] = 0;
                fileTypes[ext]++;
            }
            return fileTypes;
        }

        private string AssessImportance(string message, List<FileChange> changes, Dictionary<string, int> stats)
        {
            var msgLower = message.ToLower();

            var criticalKeywords = new[] { "fix", "bug", "security", "crash", "memory leak", "deadlock" };
            var featureKeywords = new[] { "feat", "feature", "add", "implement", "new" };
            var refactorKeywords = new[] { "refactor", "restructure", "cleanup", "optimize" };

            if (criticalKeywords.Any(kw => msgLower.Contains(kw))) return "critical";
            if (featureKeywords.Any(kw => msgLower.Contains(kw))) return "feature";

            var totalChanges = stats["Added"] + stats["Modified"] + stats["Deleted"];
            if (totalChanges > 20) return "major";

            if (refactorKeywords.Any(kw => msgLower.Contains(kw))) return "refactor";

            return "minor";
        }

        private List<string> AnalyzeImpact(List<FileChange> changes, string message)
        {
            var impacts = new List<string>();

            // 分析受影响的模块
            var modules = new Dictionary<string, int>();
            foreach (var change in changes)
            {
                var parts = change.Path.Split('/');
                if (parts.Length > 1)
                {
                    if (!modules.ContainsKey(parts[0])) modules[parts[0]] = 0;
                    modules[parts[0]]++;
                }
            }

            if (modules.Count > 0)
            {
                var moduleList = string.Join(", ", modules.OrderByDescending(m => m.Value).Take(5).Select(m => m.Key));
                impacts.Add($"受影响的模块: {moduleList}");
            }

            // 分析文件类型
            var fileTypes = GetFileTypeDistribution(changes);
            if (fileTypes.ContainsKey(".cs"))
                impacts.Add($"涉及 {fileTypes[".cs"]} 个 C# 文件变更");
            if (fileTypes.ContainsKey(".axaml") || fileTypes.ContainsKey(".xaml"))
                impacts.Add("涉及 UI/XAML 文件变更");
            if (fileTypes.ContainsKey(".md"))
                impacts.Add("涉及文档更新");

            // 根据提交消息分析
            var msgLower = message.ToLower();
            if (msgLower.Contains("fix"))
                impacts.Add("这是一个修复性提交，可能解决现有问题");
            if (msgLower.Contains("feat") || msgLower.Contains("feature"))
                impacts.Add("这是一个功能新增提交，扩展了项目能力");
            if (msgLower.Contains("refactor"))
                impacts.Add("这是一个重构提交，改善了代码结构");

            return impacts;
        }

        private List<string> GenerateReviewPoints(List<FileChange> changes, string message)
        {
            var points = new List<string>();

            // 检查关键文件
            var criticalPatterns = new[] { "Program.cs", "App.axaml", "MainWindow", "Core", "Service" };
            foreach (var change in changes)
            {
                foreach (var pattern in criticalPatterns)
                {
                    if (change.Path.Contains(pattern))
                    {
                        points.Add($"关键文件变更: {change.Path} - 需要特别关注");
                        break;
                    }
                }
            }

            // 检查提交消息质量
            if (message.Length < 10)
                points.Add("提交消息较短，建议提供更详细的变更说明");

            if (message.ToLower().Contains("wip") || message.ToLower().Contains("todo"))
                points.Add("提交包含 WIP/TODO 标记，确认是否已完成");

            // 检查文件删除
            var deleted = changes.Where(c => c.ChangeType == "deleted").ToList();
            if (deleted.Count > 0)
                points.Add($"删除了 {deleted.Count} 个文件，确认是否有其他代码依赖这些文件");

            return points;
        }

        private List<KeySnippet> GetKeySnippets(List<FileChange> changes)
        {
            var snippets = new List<KeySnippet>();

            foreach (var change in changes.Take(10))
            {
                if (change.ChangeType == "deleted") continue;

                var filePath = Path.Combine(_repoPath, change.Path);
                if (File.Exists(filePath))
                {
                    try
                    {
                        var content = File.ReadAllText(filePath, Encoding.UTF8);
                        var lines = content.Split('\n');
                        var preview = lines.Length > 30 ? string.Join("\n", lines.Take(30)) : content;

                        snippets.Add(new KeySnippet
                        {
                            File = change.Path,
                            Type = change.ChangeType,
                            LinesCount = lines.Length,
                            Preview = preview.Length > 2000 ? preview.Substring(0, 2000) : preview
                        });
                    }
                    catch
                    {
                        // 忽略无法读取的文件
                    }
                }
            }

            return snippets;
        }
    }

    public class ReportGenerator
    {
        public static string GenerateMarkdownReport(CommitAnalysis analysis)
        {
            var sb = new StringBuilder();

            // 标题
            sb.AppendLine("# Commit 深度分析报告");
            sb.AppendLine();
            sb.AppendLine($"**提交哈希**: `{analysis.CommitHash}`");
            sb.AppendLine($"**提交时间**: {analysis.Date}");
            sb.AppendLine($"**作者**: {analysis.Author} <{analysis.Email}>");
            sb.AppendLine($"**重要性**: {analysis.Importance.ToUpper()}");
            sb.AppendLine();

            // 提交消息
            sb.AppendLine("## 提交消息");
            sb.AppendLine("```");
            sb.AppendLine(analysis.Message);
            sb.AppendLine("```");
            sb.AppendLine();

            // 变更统计
            sb.AppendLine("## 变更统计");
            sb.AppendLine($"- **新增文件**: {analysis.Stats["Added"]}");
            sb.AppendLine($"- **修改文件**: {analysis.Stats["Modified"]}");
            sb.AppendLine($"- **删除文件**: {analysis.Stats["Deleted"]}");
            sb.AppendLine();

            // 文件类型分布
            if (analysis.FileTypes.Count > 0)
            {
                sb.AppendLine("### 文件类型分布");
                foreach (var ft in analysis.FileTypes.OrderByDescending(x => x.Value))
                {
                    sb.AppendLine($"- `{ft.Key}`: {ft.Value} 个文件");
                }
                sb.AppendLine();
            }

            // 变更文件列表
            if (analysis.Changes.Count > 0)
            {
                sb.AppendLine("## 变更文件列表");
                sb.AppendLine("| 文件路径 | 变更类型 |");
                sb.AppendLine("|---------|---------|");
                var typeMap = new Dictionary<string, string>
                {
                    ["added"] = "新增",
                    ["modified"] = "修改",
                    ["deleted"] = "删除"
                };
                foreach (var change in analysis.Changes.Take(50))
                {
                    var typeStr = typeMap.GetValueOrDefault(change.ChangeType, change.ChangeType);
                    sb.AppendLine($"| `{change.Path}` | {typeStr} |");
                }
                sb.AppendLine();
            }

            // 影响分析
            if (analysis.ImpactAnalysis.Count > 0)
            {
                sb.AppendLine("## 影响分析");
                foreach (var impact in analysis.ImpactAnalysis)
                {
                    sb.AppendLine($"- {impact}");
                }
                sb.AppendLine();
            }

            // 代码审查要点
            if (analysis.ReviewPoints.Count > 0)
            {
                sb.AppendLine("## 代码审查要点");
                foreach (var point in analysis.ReviewPoints)
                {
                    sb.AppendLine($"- ⚠️ {point}");
                }
                sb.AppendLine();
            }

            // 关键代码片段
            if (analysis.KeySnippets.Count > 0)
            {
                sb.AppendLine("## 关键代码片段");
                foreach (var snippet in analysis.KeySnippets.Take(5))
                {
                    sb.AppendLine($"### {snippet.File}");
                    sb.AppendLine($"- **类型**: {snippet.Type}");
                    sb.AppendLine($"- **行数**: {snippet.LinesCount}");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(snippet.Preview);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var repoPath = @"d:\github\LanMountainDesktop";
            var outputDir = Path.Combine(repoPath, "docs", "auto_commit_md");

            Console.WriteLine("Git Commit 深度分析工具");
            Console.WriteLine("======================");
            Console.WriteLine();

            // 确保输出目录存在
            Directory.CreateDirectory(outputDir);

            // 读取 HEAD 日志
            var headLogPath = Path.Combine(repoPath, ".git", "logs", "HEAD");
            if (!File.Exists(headLogPath))
            {
                Console.WriteLine($"错误: 找不到 HEAD 日志文件: {headLogPath}");
                return;
            }

            // 解析 HEAD 日志
            var commits = new List<(string Hash, string Message)>();
            var logLines = File.ReadAllLines(headLogPath);

            foreach (var line in logLines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;

                var parts = trimmedLine.Split('\t');
                if (parts.Length < 2) continue;

                var metaPart = parts[0];
                var actionPart = parts[1];

                var metaTokens = metaPart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (metaTokens.Length < 5) continue;

                var newHash = metaTokens[1];

                // 只处理 commit 操作
                if (actionPart.Contains("commit"))
                {
                    var message = actionPart.Replace("commit:", "").Trim();
                    commits.Add((newHash, message));
                }
            }

            Console.WriteLine($"找到 {commits.Count} 个 commit");
            Console.WriteLine();

            // 初始化分析器
            var analyzer = new CommitAnalyzer(repoPath);

            // 分析每个 commit
            var processed = 0;
            var success = 0;

            foreach (var commitInfo in commits)
            {
                var commitHash = commitInfo.Hash;
                var shortHash = commitHash.Substring(0, 7);
                processed++;

                Console.Write($"[{processed}/{commits.Count}] 分析 commit: {shortHash} - {commitInfo.Message.Substring(0, Math.Min(50, commitInfo.Message.Length))}");

                try
                {
                    // 分析提交
                    var analysis = analyzer.AnalyzeCommit(commitHash);
                    if (analysis == null)
                    {
                        Console.WriteLine(" [跳过]");
                        continue;
                    }

                    // 生成报告
                    var report = ReportGenerator.GenerateMarkdownReport(analysis);

                    // 保存报告
                    var dateStr = DateTimeOffset.FromUnixTimeSeconds(analysis.Timestamp).ToLocalTime().ToString("yyyyMMdd");
                    var filename = $"{dateStr}_{shortHash}_deep_analysis.md";
                    var outputFile = Path.Combine(outputDir, filename);

                    File.WriteAllText(outputFile, report, Encoding.UTF8);

                    Console.WriteLine(" [已保存]");
                    success++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" [错误: {ex.Message}]");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"分析完成! 成功处理 {success} / {processed} 个 commit");
        }
    }
}
