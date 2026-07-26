namespace DotnetAICraft.Models;

public record ReferenceResult(
    string File,
    int Line,
    int Col,
    string Context);

/// <summary>
/// One resolved symbol and its per-symbol payload (references, implementations, call graph, or
/// definition). A fully-qualified name without a parameter signature can match several overloads,
/// so refs/impls/callers/definition always return a list of these groups — one per matched symbol.
/// </summary>
public record SymbolMatchGroup<T>(
    string Symbol,
    string Kind,
    T Result);

public record CallerResult(
    string CallerSymbol,
    string CallerKind,
    bool IsDirect,
    string File,
    int Line,
    int Col,
    string Context);

public record CallGraphNode(
    string Id,
    string FullName,
    string Kind,
    string File,
    int Line,
    int Col,
    string? ContainingType,
    string? ContainingNamespace);

public record CallGraphEdge(
    string From,
    string To,
    string Relation,
    bool IsDirect);

public record CallGraphResult(
    string RootId,
    string Direction,
    int Depth,
    IReadOnlyList<CallGraphNode> Nodes,
    IReadOnlyList<CallGraphEdge> Edges);

public record RenameChange(
    string File,
    int Line,
    int Col,
    string OldText,
    string NewText);

public record RenameResult(
    string Symbol,
    string NewName,
    bool Applied,
    bool DryRun,
    IReadOnlyList<RenameChange> Changes);

public record SymbolResult(
    string Name,
    string FullName,
    string Kind,
    string File,
    int Line,
    int Col,
    string? ContainingType,
    string? ContainingNamespace);

/// <summary>
/// One node in a type's inheritance lineage tree (the <c>hierarchy</c> command). Carries the same
/// located-symbol identity fields as <see cref="SymbolResult"/> so each node is consistent with
/// <c>impls</c>, plus <see cref="Children"/> (the immediate base or derived types in the requested
/// direction) and <see cref="Truncated"/> (set when a <c>--max-depth</c> cap elides this node's
/// children rather than them being genuinely absent). Metadata base types (with
/// <c>--include-framework</c>) carry an empty <see cref="File"/> and zero line/col. See plan D1.
/// </summary>
public record HierarchyNode(
    string Name,
    string FullName,
    string Kind,
    string File,
    int Line,
    int Col,
    string? ContainingType,
    string? ContainingNamespace,
    bool Truncated,
    IReadOnlyList<HierarchyNode> Children);

public record SymbolsResultPage(
    IReadOnlyList<SymbolResult> Items,
    bool HasMore);

public record DefinitionResult(
    string FullName,
    string Kind,
    string? File,
    int? Line,
    int? Col,
    string? ContainingType,
    string? ContainingNamespace);

public record OutlineMember(
    string File,
    int Line,
    int Col,
    string DeclaringType,
    string Signature,
    string? Tag);

public record OutlineInheritedMember(
    string Signature,
    string? Tag);

public record OutlineInheritedGroup(
    string DeclaringType,
    string? Assembly,
    IReadOnlyList<OutlineInheritedMember> Members);

/// <summary>
/// The members a container declares, as flat located lines, plus (with <c>--include-inherited</c>)
/// base-class-chain members grouped under their declaring type. See plan decisions D9/D10/R9.
/// </summary>
public record OutlineResult(
    string Container,
    string Kind,
    bool PublicOnly,
    bool IncludeInherited,
    IReadOnlyList<OutlineMember> Declared,
    IReadOnlyList<OutlineInheritedGroup> Inherited);

public record SourceBlock(
    string File,
    int StartLine,
    int EndLine,
    string Text);

/// <summary>
/// Verbatim declaration text for a symbol. A <c>partial</c> type/method or set of overload parts yields
/// one <see cref="SourceBlock"/> per declaring syntax. Metadata-only or implicitly-generated symbols
/// carry <c>HasSource = false</c>, an empty block list, the declaring assembly, and a <c>Note</c>.
/// </summary>
public record SourceResult(
    string FullName,
    string Kind,
    bool HasSource,
    IReadOnlyList<SourceBlock> Blocks,
    string? Assembly,
    string? Note);

public record DescribeParameter(
    string Name,
    string Type,
    string? DefaultValue);

/// <summary>
/// Semantic card for a single symbol: <c>definition</c>'s identity/location plus signature,
/// return/parameter types, modifiers, attributes, cleaned XML-doc, and sibling overloads.
/// Metadata symbols carry null file/line/col and the declaring assembly name.
/// </summary>
public record DescribeCard(
    string FullName,
    string Kind,
    string? File,
    int? Line,
    int? Col,
    string? ContainingType,
    string? ContainingNamespace,
    string Signature,
    string? ReturnType,
    IReadOnlyList<DescribeParameter>? Parameters,
    IReadOnlyList<string>? Modifiers,
    IReadOnlyList<string>? Attributes,
    string? ConstantValue,
    string? Documentation,
    IReadOnlyList<string>? Siblings,
    string? Assembly);

public record DaemonStatus(
    bool Running,
    string SolutionPath,
    int Projects,
    int Documents,
    DateTime LoadedAt,
    TimeSpan Uptime,
    string LoadState,
    DateTime? LastLoadAttemptAt,
    string? LastLoadErrorCode,
    string? LastLoadErrorMessage);

public record DaemonShutdownResult(bool ShutdownInitiated);

public record DaemonReloadResult(
    bool Reloaded,
    string LoadState,
    DateTime LoadedAt,
    DateTime? LastLoadAttemptAt,
    string? LastLoadErrorCode,
    string? LastLoadErrorMessage);

public record ServerStatusResult(
    bool Running,
    string SolutionPath,
    DaemonStatus? Status = null,
    ErrorInfo? Error = null);

public record DiagnosticResult(
    string Project,
    string Id,
    string Severity,
    string Message,
    string? File,
    int? Line,
    int? Col,
    int? EndLine,
    int? EndCol);

public record UnusedCandidateResult(
    string Symbol,
    string Kind,
    string File,
    int Line,
    int Col,
    string Project,
    string Reason,
    double Confidence);

public record UnusedScanSummary(
    string Kind,
    string? Project,
    bool PublicOnly,
    bool IncludeGenerated,
    int Scanned,
    IReadOnlyList<UnusedCandidateResult> Items);

public record ErrorInfo(
    string Code,
    string Message,
    object? Details = null);

public record DaemonRequest(
    string Id,
    string Command,
    object? Params,
    bool? Debug = null,
    int? IdleTimeoutMinutes = null,
    PageRequest? Page = null);

public record PageRequest(
    int Offset,
    int Limit);

public record DaemonResponse<T>(
    string Id,
    DaemonResponseStatus Status,
    T? Result = default,
    ErrorInfo? Error = null,
    object? Debug = null,
    PageResponse? Page = null,
    ResponseMeta? Meta = null)
{
    public ErrorInfo? ValidateContract(string? command = null)
        => DaemonResponseContract.Validate(Status, Error, command);
}

internal static class DaemonResponseContract
{
    internal static ErrorInfo? Validate(DaemonResponseStatus status, ErrorInfo? error, string? command = null)
    {
        if (!Enum.IsDefined(status) || status == DaemonResponseStatus.NotSet)
        {
            return new ErrorInfo(
                "DAEMON_RESPONSE_INVALID_STATUS",
                "Daemon returned unsupported status value.",
                new { command, status = status.ToString().ToLowerInvariant() });
        }

        if (status == DaemonResponseStatus.Ok)
        {
            if (error is not null)
            {
                return new ErrorInfo(
                    "DAEMON_RESPONSE_CONTRACT_VIOLATION",
                    "Daemon returned ok status with non-null error payload.",
                    new { command });
            }

            return null;
        }

        if (error is null)
        {
            return new ErrorInfo(
                "DAEMON_RESPONSE_CONTRACT_VIOLATION",
                "Daemon returned non-ok status without error payload.",
                new { command, status = status.ToString().ToLowerInvariant() });
        }

        return null;
    }
}

public enum DaemonResponseStatus
{
    NotSet,
    Ok,
    Problem,
    Error
}

public record PageResponse(
    int Offset,
    int Limit,
    bool HasMore);

public record ResponseMeta(
    long DurationMs,
    DateTime SolutionLoadedAt);

public record IdleTimeoutUpdateResult(
    bool Applied,
    string Mode,
    string? Value,
    bool Changed);
