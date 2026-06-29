namespace Gateway.Public.Edge;

/// <summary>The outcome of an edge step.</summary>
/// <param name="Continue">Whether the pipeline should continue to the next step.</param>
/// <param name="Problem">The problem details when the pipeline stops; <see langword="null"/> when continuing.</param>
public sealed record EdgeStepResult(bool Continue, EdgeProblem? Problem)
{
    /// <summary>Gets a result that lets the pipeline proceed to the next step.</summary>
    public static EdgeStepResult Proceed { get; } = new(true, null);

    /// <summary>Creates a short-circuiting result carrying a problem.</summary>
    /// <param name="problem">The problem to write to the response.</param>
    /// <returns>A stop result with <see cref="Continue"/> set to <see langword="false"/>.</returns>
    public static EdgeStepResult Stop(EdgeProblem problem) => new(false, problem);
}
