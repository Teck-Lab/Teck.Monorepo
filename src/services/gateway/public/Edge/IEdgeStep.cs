namespace Gateway.Public.Edge;

/// <summary>A single-responsibility step in the edge enforcement pipeline.</summary>
public interface IEdgeStep
{
    /// <summary>Executes the step against the current request context.</summary>
    /// <param name="context">The mutable edge context for this request.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The step result indicating whether the pipeline should continue.</returns>
    Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct);
}
